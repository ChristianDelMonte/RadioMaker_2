using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Engine;

public sealed class BassEngine : IAudioEngine
{
    private readonly object _gate = new();
    private readonly List<BassBus> _buses = new();
    private readonly List<FormatPluginInfo> _plugins = new();

    /// -------------
    /// Refcount por dispositivo BASS inicializado. Clave = indice real de BASS
    /// (el que devuelve ResolveDeviceIndex), Valor = cantidad de buses que lo usan.
    /// El dispositivo se libera (Bass.Free) solo cuando el refcount llega a 0.
    ///
    /// Esto permite tener varios buses compartiendo el mismo dispositivo sin
    /// liberarlo por error, y permite tener varios dispositivos inicializados
    /// en simultaneo (BASS lo soporta).
    /// -------------
    private readonly Dictionary<int, int> _deviceRefCounts = new();

    private bool _disposed;
    private bool _initialized;
    private int _sampleRate = 44100;
    private double _globalVolume = 1.0;

    private DuckingProcessor? _duckingProcessor;
    private DuckingConfig _duckingConfig = new();

    public event EventHandler? DuckingStateChanged;

    private static readonly string[] NativeExtensions =
    {
        ".mp3", ".mp2", ".mp1", ".ogg", ".wav", ".aif", ".aiff",
        ".mo3", ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
        // ===== INTEGRACIÓN RMWave =====
        ".rmw"
    };

    public bool IsInitialized => _initialized;
    public IReadOnlyList<IAudioBus> Buses => _buses.Cast<IAudioBus>().ToList();
    public IReadOnlyList<FormatPluginInfo> LoadedPlugins => _plugins.ToList();

    public double GlobalVolume
    {
        get => _globalVolume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - _globalVolume) < 0.001) return;

            _globalVolume = clamped;

            // Aplicar a todos los canales de todos los buses.
            List<BassBus> snapshot;
            lock (_gate) snapshot = new List<BassBus>(_buses);

            foreach (var bus in snapshot)
            {
                try { bus.ApplyGlobalVolume(clamped); }
                catch { }
            }
        }
    }

    public bool IsDuckingActive => _duckingProcessor?.IsActive ?? false;
    public double CurrentDuckingGainDb => _duckingProcessor?.CurrentGainDb ?? 0.0;
    public double CurrentMicLevelDb => _duckingProcessor?.CurrentMicLevelDb ?? -120.0;

    public IReadOnlyList<string> SupportedExtensions
    {
        get
        {
            var set = new HashSet<string>(NativeExtensions, StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in _plugins.Where(p => p.IsLoaded))
                foreach (var ext in plugin.Extensions)
                    set.Add(ext);
            return set.ToList();
        }
    }

    public IAudioBus PrimaryBus =>
        _buses.FirstOrDefault(b => b.IsPrimary)
        ?? _buses.FirstOrDefault()
        ?? throw new InvalidOperationException("El motor no tiene buses. Llamaste a Initialize?");

    public BassEngine()
    {
        NativeLibraryLoader.EnsureLoaded();
    }

    // ---------------------------------------------------------------- Devices

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        var list = new List<AudioDeviceInfo>();

        int realDefault = FindDefaultDeviceIndex();
        string defaultLabel = realDefault >= 0
            ? $"Sistema predeterminado (actualmente: {GetDeviceName(realDefault)})"
            : "Sistema predeterminado del sistema";

        list.Add(new AudioDeviceInfo(-1, defaultLabel, string.Empty, true, true, false));

        for (int i = 0; i < Bass.DeviceCount; i++)
        {
            if (!Bass.GetDeviceInfo(i, out var info))
                continue;

            if (string.IsNullOrEmpty(info.Name) ||
                info.Name.Equals("No sound", StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(new AudioDeviceInfo(
                Index: i,
                Name: info.Name,
                Driver: info.Driver ?? string.Empty,
                IsEnabled: info.IsEnabled,
                IsDefault: info.IsDefault,
                IsInitialized: info.IsInitialized));
        }

        return list;
    }

    public IReadOnlyList<MicDeviceInfo> EnumerateMicrophones()
    {
        var list = new List<MicDeviceInfo>();

        int realDefault = -1;
        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            if (Bass.RecordGetDeviceInfo(i, out var info) && info.IsDefault && info.IsEnabled)
            {
                realDefault = i;
                break;
            }
        }

        string defaultLabel = realDefault >= 0
            ? $"Microfono predeterminado (actualmente: {GetMicName(realDefault)})"
            : "Microfono predeterminado del sistema";

        list.Add(new MicDeviceInfo(-1, defaultLabel, string.Empty, true, true, false));

        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            if (!Bass.RecordGetDeviceInfo(i, out var info))
                continue;

            if (string.IsNullOrEmpty(info.Name))
                continue;

            list.Add(new MicDeviceInfo(
                Index: i,
                Name: info.Name,
                Driver: info.Driver ?? string.Empty,
                IsEnabled: info.IsEnabled,
                IsDefault: info.IsDefault,
                IsInitialized: info.IsInitialized));
        }

        return list;
    }

    // --------------------------------------------------------------- Lifecycle

    public void Initialize(int deviceIndex = -1, int sampleRate = 44100)
    {
        lock (_gate)
        {
            if (_initialized) return;
            _sampleRate = sampleRate;

            LoadBassFxAddOn();

            _plugins.Clear();
            _plugins.AddRange(FormatPluginLoader.LoadAll());

            var configs = BusConfigurationStore.Load();

            if (configs.Count == 0)
            {
                var primary = new BassBus(
                    id: "primary",
                    displayName: "Principal",
                    deviceIndex: deviceIndex,
                    sampleRate: sampleRate,
                    deviceManager: this);

                primary.Initialize();
                primary.IsPrimary = true;
                _buses.Add(primary);
            }
            else
            {
                for (int i = 0; i < configs.Count; i++)
                {
                    var cfg = configs[i];
                    try
                    {
                        var bus = new BassBus(
                            id: cfg.Id,
                            displayName: cfg.DisplayName,
                            deviceIndex: cfg.DeviceIndex,
                            sampleRate: sampleRate,
                            deviceManager: this);

                        bus.IsPrimary = cfg.IsPrimary;
                        bus.MasterVolume = cfg.MasterVolume;
                        bus.Initialize();
                        _buses.Add(bus);
                    }
                    catch when (i > 0)
                    {
                        continue;
                    }
                }
            }

            // Si el bus primario configurado no pudo inicializarse, elegir
            // como primario el primero que sí quedó disponible.
            if (_buses.Count > 0 && !_buses.Any(b => b.IsPrimary))
                _buses[0].IsPrimary = true;

            _duckingProcessor = new DuckingProcessor(OnDuckingGainChanged);

            _initialized = true;
        }
    }

    public IAudioBus CreateBus(string displayName, int deviceIndex)
    {
        lock (_gate)
        {
            EnsureInitialized();

            var bus = new BassBus(
                id: Guid.NewGuid().ToString("N"),
                displayName: string.IsNullOrWhiteSpace(displayName) ? "Nuevo Bus" : displayName,
                deviceIndex: deviceIndex,
                sampleRate: _sampleRate,
                deviceManager: this);

            bus.Initialize();
            _buses.Add(bus);
            return bus;
        }
    }

    public bool RemoveBus(string busId)
    {
        lock (_gate)
        {
            if (!_initialized) return false;
            if (_buses.Count <= 1) return false;

            var bus = _buses.FirstOrDefault(b => b.Id == busId);
            if (bus is null) return false;
            if (bus.IsPrimary || bus == PrimaryBus) return false;

            _buses.Remove(bus);
            bus.Dispose();
            return true;
        }
    }

    public IAudioBus? GetBus(string busId)
    {
        lock (_gate) return _buses.FirstOrDefault(b => b.Id == busId);
    }

    // ------------------------------------------------------- Device refcount

    /// -------------
    /// Adquiere una referencia al dispositivo. Si es la primera referencia,
    /// inicializa el dispositivo con Bass.Init. Si ya estaba inicializado,
    /// solo suma el refcount.
    ///
    /// Devuelve el indice REAL resuelto (no el solicitado).
    /// -------------
    internal int AcquireDevice(int requestedIndex)
    {
        lock (_gate)
        {
            int resolved = ResolveDeviceIndex(requestedIndex);

            if (_deviceRefCounts.TryGetValue(resolved, out int count))
            {
                // Ya inicializado por otro bus — solo sumar.
                _deviceRefCounts[resolved] = count + 1;
                return resolved;
            }

            // Primer bus que usa este dispositivo — hay que inicializarlo.
            if (!TryInitDevice(resolved))
                throw new BassEngineException(
                    $"No se pudo inicializar el dispositivo {resolved} (solicitado {requestedIndex})",
                    Bass.LastError);

            _deviceRefCounts[resolved] = 1;
            return resolved;
        }
    }

    /// -------------
    /// Libera una referencia al dispositivo. Si el refcount llega a 0,
    /// se libera el dispositivo con Bass.Free.
    /// -------------
    internal void ReleaseDevice(int resolvedIndex)
    {
        lock (_gate)
        {
            if (!_deviceRefCounts.TryGetValue(resolvedIndex, out int count))
                return;

            if (count > 1)
            {
                _deviceRefCounts[resolvedIndex] = count - 1;
                return;
            }

            // Ultimo bus que lo usaba — liberar el dispositivo.
            int previousDevice = -1;
            try
            {
                previousDevice = Bass.CurrentDevice;
                if (previousDevice != resolvedIndex)
                    Bass.CurrentDevice = resolvedIndex;

                Bass.Free();
                _deviceRefCounts.Remove(resolvedIndex);
            }
            catch
            {
                // No liberar la referencia del diccionario si no se pudo
                // seleccionar/liberar el dispositivo correcto.
                _deviceRefCounts[resolvedIndex] = 1;
            }
            finally
            {
                try
                {
                    if (previousDevice >= 0 && Bass.CurrentDevice != previousDevice)
                        Bass.CurrentDevice = previousDevice;
                }
                catch { }
            }
        }
    }

    private bool TryInitDevice(int resolvedIndex)
    {
        bool ok = Bass.Init(
            resolvedIndex,
            _sampleRate,
            DeviceInitFlags.Default,
            IntPtr.Zero,
            IntPtr.Zero);

        if (ok) return true;
        if (Bass.LastError == Errors.Already) return true;
        return false;
    }

    /// -------------
    /// Resuelve un indice "solicitado" (puede ser -1) al indice real de BASS.
    /// Ver nota en versiones anteriores sobre por que -1 no es "predeterminado".
    /// -------------
    public static int ResolveDeviceIndex(int requested)
    {
        if (requested >= 0 && requested < Bass.DeviceCount)
            return requested;

        for (int i = 0; i < Bass.DeviceCount; i++)
        {
            if (Bass.GetDeviceInfo(i, out var info) && info.IsDefault && info.IsEnabled)
                return i;
        }

        for (int i = 0; i < Bass.DeviceCount; i++)
        {
            if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled)
                return i;
        }

        return 0;
    }

    // ---------------------------------------------------------------- Ducking

    public void ApplyDuckingConfig(DuckingConfig config)
    {
        if (config is null) return;

        lock (_gate)
        {
            if (!_initialized) return;

            _duckingConfig = config.Clone();

            if (_duckingProcessor is null)
                _duckingProcessor = new DuckingProcessor(OnDuckingGainChanged);

            try { _duckingProcessor.Start(_duckingConfig); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error iniciando ducking: {ex.Message}");
            }
        }

        DuckingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopDucking()
    {
        lock (_gate) _duckingProcessor?.Stop();
        DuckingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDuckingGainChanged(double gainDb)
    {
        List<BassBus> busesSnapshot;
        lock (_gate) busesSnapshot = new List<BassBus>(_buses);

        foreach (var bus in busesSnapshot)
        {
            try { bus.ApplyDuckingGain(gainDb); }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Shutdown

    public void Shutdown()
    {
        lock (_gate)
        {
            if (!_initialized) return;

            try { _duckingProcessor?.Dispose(); } catch { }
            _duckingProcessor = null;

            foreach (var bus in _buses.ToArray())
            {
                try { bus.Dispose(); } catch { }
            }
            _buses.Clear();
            _plugins.Clear();

            // Liberar cualquier dispositivo remanente que quedo con refcount > 0
            // por algun fallo durante la ejecucion.
            //
            // IMPORTANTE: Bass.Free() sin parametros libera el dispositivo del
            // HILO ACTUAL, no el de la key del diccionario. Por eso hay que
            // posicionar CurrentDevice en cada device antes de llamar a Free().
            foreach (var deviceIndex in _deviceRefCounts.Keys.ToArray())
            {
                try
                {
                    Bass.CurrentDevice = deviceIndex;
                    Bass.Free();
                }
                catch { }
            }
            _deviceRefCounts.Clear();

            // Restaurar CurrentDevice al device 0 para no dejar estado raro
            // en el hilo por si algo sigue vivo despues del Shutdown.
            try { Bass.CurrentDevice = 0; } catch { }

            _initialized = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Shutdown();
        _disposed = true;
    }

    // ---------------------------------------------------------------- Helpers

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "El motor de audio no esta inicializado. Llamar a Initialize() primero.");
    }

    private static void LoadBassFxAddOn()
    {
        try
        {
            var version = BassFx.Version;
            AppLogger.Debug("PLUGINS", $"bass_fx cargado, version: {version}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("PLUGINS",
                $"No se pudo cargar bass_fx: {ex.Message}. " +
                "El ecualizador no estara disponible.");
        }
    }

    private static int FindDefaultDeviceIndex()
    {
        for (int i = 0; i < Bass.DeviceCount; i++)
        {
            if (Bass.GetDeviceInfo(i, out var info) && info.IsDefault && info.IsEnabled)
                return i;
        }
        return -1;
    }

    private static string GetDeviceName(int index) =>
        Bass.GetDeviceInfo(index, out var info) ? info.Name : $"Dispositivo {index}";

    private static string GetMicName(int index) =>
        Bass.RecordGetDeviceInfo(index, out var info) ? info.Name : $"Microfono {index}";
}
