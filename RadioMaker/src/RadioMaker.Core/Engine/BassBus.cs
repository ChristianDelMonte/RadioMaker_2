using ManagedBass;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Engine;

/// -------------
/// Implementacion de <see cref="IAudioBus"/> sobre un dispositivo BASS.
///
/// CAMBIO IMPORTANTE (fix del cambio de dispositivo en caliente):
///   • El bus YA NO llama a Bass.Init ni Bass.Free directamente.
///   • Delega esa responsabilidad al BassEngine, que lleva un refcount por
///     dispositivo y solo libera cuando ya nadie lo usa.
///   • ChangeDevice() solo actualiza el indice activo del bus SIN tocar BASS.
///     Los canales existentes siguen reproduciendo en su dispositivo original
///     (BASS los mantiene asociados al dispositivo donde fueron creados).
///     Los canales nuevos se crean en el dispositivo nuevo.
///
/// Guardamos dos indices:
///   • _requestedDeviceIndex: lo que pidio el usuario (-1 = "sistema").
///   • _resolvedDeviceIndex:  el indice real con el que trabaja BASS.
/// -------------
internal sealed class BassBus : IAudioBus
{
    private readonly object _gate = new();
    private readonly List<BassChannel> _channels = new();
    // ===== INTEGRACIÓN RMWave =====
    private readonly List<RmwChannel> _rmwChannels = new();
    private readonly int _sampleRate;
    private readonly SyncProcedure _testToneEndSync;
    private readonly BassEngine _deviceManager;

    private bool _disposed;
    private bool _initialized;

    private int _requestedDeviceIndex;
    private int _resolvedDeviceIndex;

    private double _masterVolume = 1.0;
    private int _testToneStream;
    private TestToneGenerator? _testToneGen;
    private int _testToneEndSyncHandle;

    public string Id { get; }
    public bool IsPrimary { get; internal set; }
    public string DisplayName { get; set; }
    public int DeviceIndex => _requestedDeviceIndex;
    public int ResolvedDeviceIndex => _resolvedDeviceIndex;
    public bool IsInitialized => _initialized;

    public int ActiveChannelCount
    {
        get
        {
            int count = 0;
            foreach (var ch in _channels.ToArray())
            {
                try { if (ch.State == Abstractions.PlaybackState.Playing) count++; } catch { }
            }
            foreach (var ch in _rmwChannels.ToArray())
            {
                try { if (ch.State == Abstractions.PlaybackState.Playing) count++; } catch { }
            }
            return count;
        }
    }

    public AudioLevels GetCurrentLevels()
    {
        AudioLevels best = AudioLevels.Silent;

        foreach (var ch in _channels.ToArray())
        {
            try
            {
                if (ch.State != Abstractions.PlaybackState.Playing) continue;
                var lv = ch.Meter.Read();
                if (lv.MaxPeak > best.MaxPeak) best = lv;
            }
            catch { }
        }

        foreach (var ch in _rmwChannels.ToArray())
        {
            try
            {
                if (ch.State != Abstractions.PlaybackState.Playing) continue;
                var lv = ch.Meter.Read();
                if (lv.MaxPeak > best.MaxPeak) best = lv;
            }
            catch { }
        }

        return best;
    }

    /// -------------
    /// Nombre legible del dispositivo fisico asociado al bus.
    /// Consulta BASS en vivo para obtener el nombre actual del dispositivo.
    /// Si es el predeterminado, lo indica explicitamente.
    /// -------------
    public string DeviceDisplayName
    {
        get
        {
            try
            {
                if (_resolvedDeviceIndex < 0)
                    return "Sistema predeterminado";

                if (Bass.GetDeviceInfo(_resolvedDeviceIndex, out var info))
                {
                    var name = info.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"Dispositivo {_resolvedDeviceIndex}";

                    return _requestedDeviceIndex < 0
                        ? $"Sistema predeterminado ({name})"
                        : name;
                }
            }
            catch { }

            return _requestedDeviceIndex < 0
                ? "Sistema predeterminado"
                : $"Dispositivo {_resolvedDeviceIndex}";
        }
    }

    public double MasterVolume
    {
        get => _masterVolume;
        set => SetMasterVolume(value);
    }

    internal BassBus(
        string id,
        string displayName,
        int deviceIndex,
        int sampleRate,
        BassEngine deviceManager)
    {
        Id = id;
        DisplayName = displayName;
        _requestedDeviceIndex = deviceIndex;
        _resolvedDeviceIndex = deviceIndex;
        _sampleRate = sampleRate;
        _testToneEndSync = OnTestToneEnd;
        _deviceManager = deviceManager;
    }

    // --------------------------------------------------------------- Lifecycle

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;

            // Pedir al engine que "adquiera" el dispositivo. Si es el primero
            // que lo usa, lo inicializa. Si ya estaba inicializado, solo suma
            // el refcount y devuelve el indice real.
            _resolvedDeviceIndex = _deviceManager.AcquireDevice(_requestedDeviceIndex);
            _initialized = true;
        }
    }

    /// -------------
    /// Cambia el dispositivo de salida del bus SIN interrumpir los canales activos.
    ///
    /// Los canales existentes siguen reproduciendo en su dispositivo original.
    /// Los canales nuevos se crearan en el nuevo dispositivo.
    /// -------------
    public bool ChangeDevice(int deviceIndex)
    {
        lock (_gate)
        {
            if (_disposed) return false;

            int newResolved = BassEngine.ResolveDeviceIndex(deviceIndex);

            // Si es el mismo dispositivo real, no hacemos nada.
            if (newResolved == _resolvedDeviceIndex && _initialized)
            {
                _requestedDeviceIndex = deviceIndex;
                return true;
            }

            // Liberar el dispositivo anterior (baja el refcount; si llega a 0,
            // el engine lo libera con Bass.Free, pero solo si nadie mas lo usa).
            if (_initialized)
            {
                _deviceManager.ReleaseDevice(_resolvedDeviceIndex);
                _initialized = false;
            }

            // Adquirir el nuevo (lo inicializa si hace falta).
            try
            {
                _resolvedDeviceIndex = _deviceManager.AcquireDevice(deviceIndex);
                _initialized = true;
                _requestedDeviceIndex = deviceIndex;
                return true;
            }
            catch
            {
                // Fallo al adquirir el nuevo: intentar restaurar el anterior.
                try
                {
                    _resolvedDeviceIndex = _deviceManager.AcquireDevice(_requestedDeviceIndex);
                    _initialized = true;
                }
                catch { }
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            StopTestToneCore();
            DisposeAllChannels();

            if (_initialized)
            {
                _deviceManager.ReleaseDevice(_resolvedDeviceIndex);
                _initialized = false;
            }
        }
    }

    // ------------------------------------------------------------------ Files

    public IAudioChannel LoadFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Se requiere una ruta de archivo.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Archivo de audio no encontrado.", filePath);

        lock (_gate)
        {
            EnsureInitialized();

            // ===== INTEGRACIÓN RMWave =====
            if (RmwLoader.IsRmwFile(filePath))
                return LoadRmwFile(filePath);

            // Los canales nuevos se crean en el dispositivo ACTUAL del bus.
            int handle = WithDeviceContext(() =>
                Bass.CreateStream(filePath, 0L, 0L, BassFlags.Default));

            if (handle == 0)
            {
                var err = Bass.LastError;
                throw new BassEngineException(
                    $"Bass.CreateStream fallo para '{Path.GetFileName(filePath)}'", err);
            }

            // ===== FIX trimEnd =====
            // Sin Prescan, BASS puede devolver Duration=0 o incompleto para
            // algunos formatos (MP3 VBR sin Xing, archivos con header raro).
            // Cuando SetTrim corre con Duration=0, el sync de trimEnd no se
            // registra y el audio suena completo.
            //
            // Verificamos: si ChannelGetLength devuelve <= 0.5s, reabrimos
            // con Prescan (que escanea todo el archivo y garantiza Duration
            // correcto). Solo lo pagamos cuando hace falta.
            double durationSecs = GetStreamDurationSeconds(handle);
            if (durationSecs <= 0.5)
            {
                Bass.StreamFree(handle);

                handle = WithDeviceContext(() =>
                    Bass.CreateStream(filePath, 0L, 0L, BassFlags.Default | BassFlags.Prescan));

                if (handle == 0)
                {
                    var err = Bass.LastError;
                    throw new BassEngineException(
                        $"Bass.CreateStream (Prescan) fallo para '{Path.GetFileName(filePath)}'", err);
                }
            }

            var channel = new BassChannel(
                handle,
                filePath,
                Id,
                _masterVolume,
                onDisposed: ch => { lock (_gate) _channels.Remove(ch); });

            // Aplicar el volumen global vigente al canal nuevo.
            try { channel.SetGlobalVolume(_deviceManager.GlobalVolume); } catch { }

            _channels.Add(channel);
            return channel;
        }
    }

    // ===== INTEGRACIÓN RMWave =====
    private RmwChannel LoadRmwFile(string filePath)
    {
        return WithDeviceContext(() =>
        {
            var channel = new RmwChannel(
                filePath,
                Id,
                _masterVolume,
                _deviceManager.GlobalVolume,
                onDisposed: ch => { lock (_gate) _rmwChannels.Remove(ch); });

            _rmwChannels.Add(channel);
            return channel;
        });
    }

    // --------------------------------------------------------------- Test tone

    public void PlayTestTone(double frequencyHz = 440.0, int durationMs = 1000)
    {
        lock (_gate)
        {
            EnsureInitialized();
            StopTestToneCore();

            var gen = new TestToneGenerator(frequencyHz, _sampleRate, durationMs);
            int handle = WithDeviceContext(gen.CreateStream);

            if (handle == 0)
                throw new BassEngineException(
                    "No se pudo crear el stream del tono de prueba", Bass.LastError);

            _testToneStream = handle;
            _testToneGen = gen;
            _testToneEndSyncHandle = Bass.ChannelSetSync(
                handle, SyncFlags.End, 0L, _testToneEndSync, IntPtr.Zero);

            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)GetTestToneVolume());
            Bass.ChannelPlay(handle, false);
        }
    }

    private void StopTestToneCore()
    {
        if (_testToneStream == 0) return;

        try
        {
            Bass.ChannelStop(_testToneStream);
            if (_testToneEndSyncHandle != 0)
                Bass.ChannelRemoveSync(_testToneStream, _testToneEndSyncHandle);
            Bass.StreamFree(_testToneStream);
        }
        catch { }

        _testToneStream = 0;
        _testToneEndSyncHandle = 0;
        _testToneGen = null;
    }

    private void OnTestToneEnd(int handle, int channel, int data, IntPtr user)
    {
        lock (_gate) StopTestToneCore();
    }

    /// -------------
    /// Reproduce una secuencia de N pulsos de M ms separados por K ms.
    /// Se usa para el beep de emergencia (4 pulsos de 500ms, amplitud 0.75).
    /// No interfiere con la reproduccion en curso del bus: se suma como
    /// un stream adicional.
    /// -------------
    public void PlayTestToneSequence(
        double frequencyHz,
        int pulseMs,
        int gapMs,
        int pulseCount,
        double amplitude = 0.75)
    {
        lock (_gate)
        {
            EnsureInitialized();

            // Detener el beep anterior si todavia estaba sonando (evita
            // superposicion si dos secuencias se disparan muy cerca).
            StopTestToneCore();

            var gen = new PulseSequenceGenerator(
                frequencyHz, _sampleRate, pulseMs, gapMs, pulseCount, amplitude);

            int handle = WithDeviceContext(gen.CreateStream);

            if (handle == 0)
            {
                AppLogger.Warning("EMERGENCY",
                    $"No se pudo crear stream de beep: {Bass.LastError}");
                return;
            }

            _testToneStream = handle;
            _testToneGen = null; // no aplica (generador distinto)
            _testToneEndSyncHandle = Bass.ChannelSetSync(
                handle, SyncFlags.End, 0L, _testToneEndSync, IntPtr.Zero);

            // La amplitud del pulso y los volúmenes del bus/engine se aplican
            // como ganancias separadas.
            Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)GetTestToneVolume());
            Bass.ChannelPlay(handle, false);

            AppLogger.Debug("EMERGENCY",
                $"Beep iniciado: {pulseCount} pulso(s) de {pulseMs}ms @ {frequencyHz}Hz.");
        }
    }

    // ------------------------------------------------------------- Volumen master

    private void SetMasterVolume(double value)
    {
        double clamped = Math.Clamp(value, 0.0, 1.0);

        lock (_gate)
        {
            _masterVolume = clamped;

            foreach (var ch in _channels.ToArray())
            {
                try { ch.ApplyBusVolume(clamped); } catch { }
            }

            // ===== INTEGRACIÓN RMWave =====
            foreach (var ch in _rmwChannels.ToArray())
            {
                try { ch.ApplyBusVolume(clamped); } catch { }
            }

            if (_testToneStream != 0)
                Bass.ChannelSetAttribute(_testToneStream, ChannelAttribute.Volume, (float)GetTestToneVolume());
        }
    }

    /// -------------
    /// Aplica una ganancia de ducking (dB) a todos los canales del bus.
    /// Invocado desde BassEngine a 50 Hz.
    /// -------------
    internal void ApplyDuckingGain(double gainDb)
    {
        foreach (var ch in _channels.ToArray())
        {
            try { ch.SetDuckingGain(gainDb); }
            catch { }
        }
        // ===== INTEGRACIÓN RMWave =====
        foreach (var ch in _rmwChannels.ToArray())
        {
            try { ch.SetDuckingGain(gainDb); } catch { }
        }
    }

    /// -------------
    /// Aplica el volumen global (0..1) a todos los canales del bus.
    /// Invocado desde BassEngine cuando el usuario mueve el slider master.
    /// -------------
    internal void ApplyGlobalVolume(double volume)
    {
        foreach (var ch in _channels.ToArray())
        {
            try { ch.SetGlobalVolume(volume); }
            catch { }
        }
        // ===== INTEGRACIÓN RMWave =====
        foreach (var ch in _rmwChannels.ToArray())
        {
            try { ch.SetGlobalVolume(volume); }
            catch { }
        }

        lock (_gate)
        {
            if (_testToneStream != 0)
            {
                try
                {
                    Bass.ChannelSetAttribute(
                        _testToneStream, ChannelAttribute.Volume, (float)GetTestToneVolume());
                }
                catch { }
            }
        }
    }

    private double GetTestToneVolume() =>
        Math.Clamp(_masterVolume * _deviceManager.GlobalVolume, 0.0, 1.0);

    // ----------------------------------------------------------------- Helpers

    private static double GetStreamDurationSeconds(int handle)
    {
        try
        {
            long lenBytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
            if (lenBytes <= 0) return 0;
            return Bass.ChannelBytes2Seconds(handle, lenBytes);
        }
        catch { return 0; }
    }

    private void EnsureInitialized()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BassBus));
        if (!_initialized)
            throw new InvalidOperationException($"El bus '{DisplayName}' no esta inicializado.");
    }

    /// -------------
    /// Ejecuta <paramref name="action"/> cambiando temporalmente el CurrentDevice
    /// del hilo actual al de este bus. BASS usa estado thread-local para
    /// CurrentDevice, asi que restauramos el valor previo al salir.
    /// -------------
    private T WithDeviceContext<T>(Func<T> action)
    {
        int previous = Bass.CurrentDevice;
        try
        {
            if (previous != _resolvedDeviceIndex)
                Bass.CurrentDevice = _resolvedDeviceIndex;
            return action();
        }
        finally
        {
            if (Bass.CurrentDevice != previous)
                Bass.CurrentDevice = previous;
        }
    }

    private void DisposeAllChannels()
    {
        foreach (var ch in _channels.ToArray())
        {
            try { ch.Dispose(); } catch { }
        }
        _channels.Clear();

        // ===== INTEGRACIÓN RMWave =====
        foreach (var ch in _rmwChannels.ToArray())
        {
            try { ch.Dispose(); } catch { }
        }
        _rmwChannels.Clear();
    }

    internal void UnregisterChannel(BassChannel channel) => _channels.Remove(channel);
}
