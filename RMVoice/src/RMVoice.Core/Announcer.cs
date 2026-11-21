using RMVoice.Core.Models;
using RMVoice.Core.Services;

namespace RMVoice.Core;

/// <summary>
/// Implementación por defecto de <see cref="IAnnouncer"/>.
/// Orquesta AudioService + ProfileStore + AppSettingsStore + WeatherService.
/// </summary>
public sealed class Announcer : IAnnouncer
{
    private readonly AnnouncerOptions _options;
    private readonly AudioService _audio;
    private readonly ProfileStore _profileStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly WeatherService _weather;
    private readonly List<VoiceProfile> _profiles = new();

    private readonly Dictionary<TrackKind, List<AudioRegion>> _regions = new()
    {
        [TrackKind.Hour] = new(),
        [TrackKind.Minute] = new(),
        [TrackKind.Temperature] = new(),
        [TrackKind.Humidity] = new(),
    };

    private VoiceProfile? _activeProfile;
    private WeatherSnapshot? _lastWeather;
    private WeatherLocation? _activeWeatherLocation;
    private bool _initialized;
    private bool _disposed;

    public event EventHandler<AnnounceRequest>? PlaybackStarted;
    public event EventHandler<AnnounceRequest>? PlaybackCompleted;
    public event EventHandler<Exception>? PlaybackFailed;

    public Announcer(AnnouncerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _audio = new AudioService(options.OwnsBass)
        {
            DefaultDevice = options.DefaultDeviceIndex,
            Volume = options.InitialVolume
        };

        _profileStore = new ProfileStore(
            options.ProfilesPath ?? EnginePaths.DefaultProfilesPath());

        _settingsStore = new AppSettingsStore(
            options.SettingsPath ?? EnginePaths.DefaultSettingsPath());

        _weather = new WeatherService(options.UserAgent);
    }

    // ---------------------------------------------------------------------
    //  Estado
    // ---------------------------------------------------------------------

    public bool IsInitialized => _initialized;
    public bool IsPlaying => _audio.IsPlaying;
    public int CurrentStreamHandle => _audio.CurrentStreamHandle;

    public int DefaultDevice
    {
        get => _audio.DefaultDevice;
        set => _audio.DefaultDevice = value;
    }

    public float Volume
    {
        get => _audio.Volume;
        set => _audio.Volume = value;
    }

    public IReadOnlyList<VoiceProfile> Profiles => _profiles;

    public VoiceProfile? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (ReferenceEquals(_activeProfile, value)) return;
            _activeProfile = value;
            LoadProfileAudio(value);
        }
    }

    public WeatherSnapshot? LastWeather => _lastWeather;

    /// <summary>
    /// Ubicación de clima en memoria. Persistencia la maneja el host
    /// (RMVoice.exe desde su UI, o RadioMaker si quiere).
    /// </summary>
    public WeatherLocation? ActiveWeatherLocation
    {
        get => _activeWeatherLocation;
        set => _activeWeatherLocation = value;
    }

    // ---------------------------------------------------------------------
    //  Ciclo de vida
    // ---------------------------------------------------------------------

    public void Initialize()
    {
        if (_initialized) return;

        // Cargar perfiles y settings PRIMERO. Si _audio.Init() falla
        // despues (por ejemplo porque no hay device de audio o porque
        // el usuario no tiene BASS disponible), la UI ya tiene los
        // perfiles guardados y puede mostrarlos / editarlos.
        LoadProfilesFromDisk();
        LoadSettingsFromDisk();

        _audio.Init();

        if (_profiles.Count > 0)
            ActiveProfile = _profiles[0];

        _initialized = true;
    }

    public void ReloadProfiles()
    {
        var currentName = _activeProfile?.Name;
        LoadProfilesFromDisk();

        var target = _profiles.FirstOrDefault(p => p.Name == currentName)
                     ?? _profiles.FirstOrDefault();

        _activeProfile = null;
        ActiveProfile = target;
    }

    public void ReloadActiveProfile()
    {
        var current = _activeProfile;
        _activeProfile = null;
        ActiveProfile = current;
    }

    private void LoadProfilesFromDisk()
    {
        _profiles.Clear();

        var loaded = _profileStore.Load();

        if (loaded.Count == 0)
        {
            loaded.Add(new VoiceProfile
            {
                Name = "Español · Ejemplo",
                Language = "es",
                Gender = "—",
                HourTablePath = BundledAsset("Hora", "Hombre", "H_Esp_Comp.txt"),
                MinuteTablePath = BundledAsset("Hora", "Hombre", "M_Esp_Comp.txt"),
                TemperatureTablePath = BundledAsset("Clima", "Hombre", "T_Esp_Comp.txt"),
                HumidityTablePath = BundledAsset("Clima", "Hombre", "Hu_Esp_Comp.txt"),
                HourAudioPath = BundledAsset("Hora", "Hombre", "H_Esp_Comp.mp3"),
                MinuteAudioPath = BundledAsset("Hora", "Hombre", "M_Esp_Comp.mp3"),
                TemperatureAudioPath = BundledAsset("Clima", "Hombre", "T_Esp_Comp.mp3"),
                HumidityAudioPath = BundledAsset("Clima", "Hombre", "Hu_Esp_Comp.mp3"),
            });
        }

        _profiles.AddRange(loaded);
    }

    private void LoadSettingsFromDisk()
    {
        var s = _settingsStore.Load();
        _activeWeatherLocation = s.ActiveWeatherLocation;

        // Reconstruir el último snapshot de clima si fue persistido.
        // Permite que la UI muestre temperatura/humedad al abrir, sin
        // necesidad de una nueva consulta a Open-Meteo.
        if (s.LastTemperature.HasValue && s.LastHumidity.HasValue)
        {
            _lastWeather = new WeatherSnapshot
            {
                Timestamp = s.LastWeatherTimestamp ?? DateTime.Now,
                Temperature = s.LastTemperature.Value,
                Humidity = s.LastHumidity.Value
            };
        }
    }

    private void LoadProfileAudio(VoiceProfile? profile)
    {
        foreach (var list in _regions.Values)
            list.Clear();

        if (profile is null)
        {
            foreach (TrackKind kind in Enum.GetValues(typeof(TrackKind)))
                _audio.UnloadTrack(kind);
            return;
        }

        LoadSide(TrackKind.Hour, profile.HourTablePath, profile.HourAudioPath);
        LoadSide(TrackKind.Minute, profile.MinuteTablePath, profile.MinuteAudioPath);
        LoadSide(TrackKind.Temperature, profile.TemperatureTablePath, profile.TemperatureAudioPath);
        LoadSide(TrackKind.Humidity, profile.HumidityTablePath, profile.HumidityAudioPath);
    }

    private void LoadSide(TrackKind kind, string tablePath, string audioPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(tablePath) && File.Exists(tablePath))
                _regions[kind].AddRange(RegionTableParser.Parse(tablePath));

            if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
                _audio.LoadTrack(kind, audioPath);
            else
                _audio.UnloadTrack(kind);
        }
        catch
        {
            _audio.UnloadTrack(kind);
        }
    }

    // ---------------------------------------------------------------------
    //  Reproducción
    // ---------------------------------------------------------------------

    public async Task PlayAsync(AnnounceRequest request, CancellationToken ct = default)
    {
        if (!_initialized)
            throw new InvalidOperationException("Announcer no inicializado.");

        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.ThrowIfBusy && _audio.IsPlaying)
            throw new InvalidOperationException("El Engine ya está reproduciendo otra locución.");

        var segments = BuildSegments(request);

        if (segments.Count == 0)
        {
            PlaybackFailed?.Invoke(this,
                new InvalidOperationException("No hay segmentos válidos para reproducir."));
            return;
        }

        PlaybackStarted?.Invoke(this, request);

        try
        {
            var device = request.DeviceIndex >= 0 ? request.DeviceIndex : _audio.DefaultDevice;
            await _audio.PlaySequenceAsync(segments, device, request.Volume, ct)
                        .ConfigureAwait(false);

            PlaybackCompleted?.Invoke(this, request);
        }
        catch (Exception ex)
        {
            PlaybackFailed?.Invoke(this, ex);
            throw;
        }
    }

    public void Play(AnnounceRequest request)
        => PlayAsync(request).GetAwaiter().GetResult();

    public void Stop() => _audio.Stop();

    private List<Segment> BuildSegments(AnnounceRequest request)
    {
        var result = new List<Segment>();
        var content = request.Content;
        var now = DateTime.Now;

        if (content.HasFlag(AnnounceContent.Hour))
        {
            var hour = request.Hour ?? now.Hour;
            AddSegment(result, TrackKind.Hour, Math.Clamp(hour, 0, 23));
        }

        if (content.HasFlag(AnnounceContent.Minute))
        {
            var minute = request.Minute ?? now.Minute;
            AddSegment(result, TrackKind.Minute, Math.Clamp(minute, 0, 59));
        }

        if (content.HasFlag(AnnounceContent.Temperature))
        {
            var temp = request.Temperature
                       ?? (int)Math.Round(_lastWeather?.Temperature ?? 20);
            temp = Math.Clamp(temp, -30, 50);
            AddSegment(result, TrackKind.Temperature, temp);
        }

        if (content.HasFlag(AnnounceContent.Humidity))
        {
            var hum = request.Humidity
                      ?? (int)Math.Round(_lastWeather?.Humidity ?? 50);
            hum = Math.Clamp(hum, 5, 100);
            AddSegment(result, TrackKind.Humidity, hum);
        }

        return result;
    }

    private void AddSegment(List<Segment> target, TrackKind kind, int index)
    {
        if (!_audio.HasTrack(kind))
            return;

        var region = _regions[kind].FirstOrDefault(r => r.Index == index);
        if (region is null)
            return;

        target.Add(new Segment(kind, region));
    }

    // ---------------------------------------------------------------------
    //  Clima
    // ---------------------------------------------------------------------

    public Task<List<WeatherLocation>> SearchLocationsAsync(string query, CancellationToken ct = default)
        => _weather.SearchAsync(query, ct);

    public async Task<WeatherSnapshot> FetchWeatherAsync(CancellationToken ct = default)
    {
        if (_activeWeatherLocation is null)
            throw new InvalidOperationException(
                "No hay ubicación activa. Configúrala desde el EXE (pestaña Clima).");

        var snap = await _weather.FetchCurrentAsync(_activeWeatherLocation, ct)
                                 .ConfigureAwait(false);
        _lastWeather = snap;

        // Persistir el snapshot en settings.json para que sobreviva al
        // cierre de la app. Al reabrir, LoadSettingsFromDisk reconstruye
        // _lastWeather sin necesidad de consultar la API de nuevo.
        PersistLastWeather(snap);

        return snap;
    }

    /// <summary>
    /// Guarda el último snapshot de clima en settings.json. Preserva el
    /// resto de los ajustes (ubicación, auto-refresh, etc).
    /// </summary>
    private void PersistLastWeather(WeatherSnapshot snapshot)
    {
        try
        {
            var s = _settingsStore.Load();
            s.LastTemperature = snapshot.Temperature;
            s.LastHumidity = snapshot.Humidity;
            s.LastWeatherTimestamp = snapshot.Timestamp;
            _settingsStore.Save(s);
        }
        catch { }
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// Resuelve la ruta de un asset empaquetado con el engine. Busca en
    /// dos ubicaciones posibles:
    ///
    ///   1. Assets/&lt;segments&gt; (relativo a AppContext.BaseDirectory)
    ///      → funciona cuando RMVoice.exe corre standalone desde Addons/RMVoice/
    ///      → funciona tambien cuando RadioMaker tiene los assets copiados
    ///        en su propio Assets/
    ///
    ///   2. Addons/RMVoice/Assets/&lt;segments&gt;
    ///      → funciona cuando RadioMaker llama al engine y NO tiene los
    ///        assets en su propio Assets/ (solo estan en Addons/RMVoice/)
    ///
    /// Si no encuentra el archivo en ninguna ubicacion, devuelve string vacio.
    /// </summary>
    private static string BundledAsset(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
            return string.Empty;

        var baseDirs = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "Addons", "RMVoice"),
        };

        foreach (var baseDir in baseDirs)
        {
            var parts = new List<string> { baseDir, "Assets" };
            parts.AddRange(segments);

            try
            {
                var path = Path.Combine(parts.ToArray());
                if (File.Exists(path)) return path;
            }
            catch { }
        }

        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.Dispose();
        _weather.Dispose();
    }
}
