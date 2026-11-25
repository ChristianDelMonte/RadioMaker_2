using RMVoice.Core;
using RMVoice.Core.Models;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Engine;

namespace RadioMaker.Core.Playback;

/// -------------
/// Servicio que envuelve el motor de locución de RMVoice.
///
/// Responsabilidades:
///   - Inicializar el <see cref="IAnnouncer"/> una vez al arrancar RadioMaker
///     con <c>OwnsBass = false</c> (RadioMaker ya tiene BASS inicializado).
///   - Exponer métodos semánticos (PlayHourAsync, PlayClimateAsync, etc.)
///     que mapean a los comandos de RadioMaker.
///   - Asegurar que el device de reproducción sea el MISMO del bus donde
///     suena la pista principal.
///   - Cachear y refrescar el clima (llamando a FetchWeatherAsync).
///   - Reportar fallos de inicialización o de red sin romper la app.
///
/// El servicio es TOLERANTE A FALLOS: si RMVoice.Engine no está disponible
/// o falla al inicializar, <see cref="IsAvailable"/> queda en false y todos
/// los Play* devuelven false. La app sigue funcionando con normalidad.
/// -------------
public sealed class LocutionService : IDisposable
{
    private readonly object _gate = new();
    private IAnnouncer? _announcer;
    private bool _available;
    private bool _disposed;
    private int _monitorHandle;
    private BassLevelMeter? _monitorMeter;
    private BassSpectrum? _monitorSpectrum;
    public string InterfaceLanguage { get; set; } = "es";

    public LocutionService() { }

    /// -------------True si RMVoice.Engine está cargado e inicializado.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate) return _available && _announcer?.IsInitialized == true;
        }
    }

    /// -------------True si hay una locución reproduciéndose ahora.</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _announcer?.IsPlaying == true;
        }
    }

    public IAudioLevelMeter? CurrentMeter => GetCurrentMonitor().Meter;
    public IAudioSpectrum? CurrentSpectrum => GetCurrentMonitor().Spectrum;

    private (IAudioLevelMeter? Meter, IAudioSpectrum? Spectrum) GetCurrentMonitor()
    {
        lock (_gate)
        {
            var handle = _announcer?.CurrentStreamHandle ?? 0;
            if (_disposed || handle == 0) return (null, null);

            if (_monitorHandle != handle)
            {
                _monitorHandle = handle;
                _monitorMeter = new BassLevelMeter(handle);
                _monitorSpectrum = new BassSpectrum(handle);
            }

            return (_monitorMeter, _monitorSpectrum);
        }
    }

    /// -------------Se dispara cuando cambia el estado de disponibilidad.</summary>
    public event EventHandler? StateChanged;

    // ───── Ciclo de vida ─────────────────────────────────────────────────

    /// -------------
    /// Inicializa el motor de locución. Es idempotente.
    /// Se llama una vez al arranque de RadioMaker (durante el splash).
    ///
    /// IMPORTANTE: llamar DESPUÉS de que RadioMaker inicializó BASS
    /// (BassEngine.Initialize).
    /// -------------
    public void Initialize()
    {
        lock (_gate)
        {
            if (_announcer is not null) return;

            try
            {
                var options = new AnnouncerOptions
                {
                    OwnsBass = false,
                    UserAgent = "RadioMaker/1.0",
                    InterfaceLanguage = InterfaceLanguage
                };

                _announcer = new Announcer(options);
                _announcer.Initialize();

                _available = _announcer.IsInitialized;

                if (_available)
                {
                    AppLogger.Info("LOCUTION",
                        $"RMVoice inicializado. {_announcer.Profiles.Count} perfil(es) de voz.");
                }
                else
                {
                    AppLogger.Warning("LOCUTION",
                        "RMVoice.Initialize() completó pero IsInitialized sigue false.");
                }
            }
            catch (Exception ex)
            {
                _available = false;
                AppLogger.Error("LOCUTION",
                    $"No se pudo inicializar RMVoice: {ex.Message}", ex);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ───── Reproducción ──────────────────────────────────────────────────

    /// -------------
    /// Reproduce un comando de locución en el device indicado.
    /// El await termina cuando la locución acaba.
    /// Devuelve false si RMVoice no está disponible o si falló.
    /// -------------
    public async Task<bool> PlayAsync(
        LocutionCommand command,
        int deviceIndex,
        float volume = 1f,
        CancellationToken ct = default)
    {
        IAnnouncer? announcer;
        lock (_gate)
        {
            if (_disposed) return false;
            if (!_available || _announcer is null) return false;
            announcer = _announcer;
        }

        var content = MapCommand(command);

        try
        {
            var request = new AnnounceRequest
            {
                Content = content,
                DeviceIndex = deviceIndex,
                Volume = Math.Clamp(volume, 0f, 1f)
            };

            // RMVoice recibe el dispositivo explícitamente y lo aplica a sus
            // streams. CurrentDevice es estado local al hilo y no debe
            // mantenerse cambiado a través de un await.
            await announcer.PlayAsync(request, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("LOCUTION",
                $"Error al reproducir locución {command}: {ex.Message}");
            return false;
        }
    }

    /// -------------Corta la locución en curso (si la hay).</summary>
    public void Stop()
    {
        try
        {
            IAnnouncer? announcer;
            lock (_gate) announcer = _announcer;
            announcer?.Stop();
        }
        catch { }
    }

    // ───── Clima ─────────────────────────────────────────────────────────

    /// -------------
    /// Refresca el clima consultando la API (Open-Meteo) ahora mismo.
    /// Es seguro llamarlo aunque no haya ubicación configurada: solo
    /// loguea un warning.
    /// -------------
    public async Task RefreshWeatherAsync(CancellationToken ct = default)
    {
        IAnnouncer? announcer;
        lock (_gate)
        {
            if (_disposed || !_available || _announcer is null) return;
            announcer = _announcer;
        }

        try
        {
            await announcer.FetchWeatherAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("LOCUTION",
                $"No se pudo refrescar el clima: {ex.Message}");
        }
    }

    /// -------------Último snapshot de clima cacheado por el engine (o null).</summary>
    public WeatherSnapshot? LastWeather
    {
        get
        {
            lock (_gate) return _announcer?.LastWeather;
        }
    }

    /// -------------Ubicación de clima configurada (o null).</summary>
    public WeatherLocation? ActiveWeatherLocation
    {
        get
        {
            lock (_gate) return _announcer?.ActiveWeatherLocation;
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private static AnnounceContent MapCommand(LocutionCommand command) => command switch
    {
        LocutionCommand.HourAndMinute => AnnounceContent.HourAndMinute,
        LocutionCommand.Climate => AnnounceContent.ClimateOnly,
        LocutionCommand.HourAndClimate => AnnounceContent.HourMinuteAndClimate,
        _ => AnnounceContent.HourAndMinute
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            try { _announcer?.Dispose(); } catch { }
            _announcer = null;
            _available = false;
            _monitorHandle = 0;
            _monitorMeter = null;
            _monitorSpectrum = null;
        }
    }
}
