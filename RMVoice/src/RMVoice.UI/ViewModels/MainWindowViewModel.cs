using System.Collections.ObjectModel;
using System.Globalization;
using RMVoice.UI.Models;
using RMVoice.UI.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVoice.Core;
using RMVoice.Core.Models;
using RMVoice.Core.Services;
using RMVoice.UI.Localization;

namespace RMVoice.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly string[] AudioPatterns = { "*.wav", "*.mp3", "*.ogg", "*.flac", "*.aiff", "*.aif" };
    private static readonly string[] TablePatterns = { "*.txt" };

    private readonly IAnnouncer _announcer;
    private readonly IDialogService _dialogs;
    private readonly ProfileStore _profileStore;
    private readonly DispatcherTimer _clockTimer;

    private int _lastAutoAnnouncedMinute = -1;
    private bool _suppressProfileApply;
    private bool _disposed;

    /// <summary>
    /// True mientras no se haya completado el primer fetch del arranque.
    /// Se usa para NO disparar el anuncio automático del primer fetch
    /// (que fue silencioso, disparado por nosotros, no por el usuario).
    /// </summary>
    private bool _isFirstWeatherFetch;

    public enum AnnounceMode
    {
        HourMinute,
        ClimateOnly,
        HourMinuteAndClimate
    }

    // ------------------------------------------------------------------ //
    //  Colecciones
    // ------------------------------------------------------------------ //

    public ObservableCollection<VoiceProfile> Profiles { get; } = new();
    public ObservableCollection<AudioRegion> HourRegions { get; } = new();
    public ObservableCollection<AudioRegion> MinuteRegions { get; } = new();
    public ObservableCollection<AudioRegion> TemperatureRegions { get; } = new();
    public ObservableCollection<AudioRegion> HumidityRegions { get; } = new();

    public IReadOnlyList<IndexOption> Hours { get; } =
        Enumerable.Range(0, 24).Select(i => new IndexOption(i)).ToList();

    public IReadOnlyList<IndexOption> Minutes { get; } =
        Enumerable.Range(0, 60).Select(i => new IndexOption(i)).ToList();

    public IReadOnlyList<AnnounceMode> AnnounceModes { get; } =
        new[] { AnnounceMode.HourMinute, AnnounceMode.ClimateOnly, AnnounceMode.HourMinuteAndClimate };

    public WeatherPanelViewModel Weather { get; }

    // ------------------------------------------------------------------ //
    //  Estado observable
    // ------------------------------------------------------------------ //

    [ObservableProperty] private VoiceProfile? _selectedProfile;
    [ObservableProperty] private IndexOption _selectedHour;
    [ObservableProperty] private IndexOption _selectedMinute;

    [ObservableProperty] private decimal _temperature = 22m;
    [ObservableProperty] private decimal _humidity = 60m;

    [ObservableProperty] private AnnounceMode _autoAnnounceMode = AnnounceMode.HourMinute;
    [ObservableProperty] private AnnounceMode _manualMode = AnnounceMode.HourMinuteAndClimate;

    [ObservableProperty] private string _currentTimeText = "--:--:--";
    [ObservableProperty] private string _currentDateText = "";
    [ObservableProperty] private string _climateSummary = "— °C · — %";
    [ObservableProperty] private string _statusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.Starting");

    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _autoAnnounce;
    [ObservableProperty] private bool _isDarkTheme;

    public string VolumeText => $"{Volume:0} %";

    // ------------------------------------------------------------------ //

    public MainWindowViewModel(IDialogService dialogs, IAnnouncer announcer)
    {
        _dialogs = dialogs;
        _announcer = announcer;
        VoiceLocalizationService.LanguageChanged += OnLanguageChanged;
        _profileStore = new ProfileStore();

        _selectedHour = Hours[DateTime.Now.Hour];
        _selectedMinute = Minutes[DateTime.Now.Minute];

        // Suscribir a eventos del Engine.
        _announcer.PlaybackStarted += OnPlaybackStarted;
        _announcer.PlaybackCompleted += OnPlaybackCompleted;
        _announcer.PlaybackFailed += OnPlaybackFailed;

        // Panel de clima.
        Weather = new WeatherPanelViewModel(_announcer);
        Weather.WeatherUpdated += OnWeatherUpdated;
        Weather.FetchFailed += OnWeatherFetchFailed;

        StatusMessage = _announcer.IsInitialized
            ? VoiceLocalizationService.GetString("Loc.RMVoice.Status.BassReady")
            : VoiceLocalizationService.GetString("Loc.RMVoice.Status.BassUnavailable");

        LoadProfiles();

        // Inicializar temperatura y humedad con el último snapshot
        // persistido (si existe). Si no existe pero hay ubicación
        // configurada, dispara un fetch silencioso en background para
        // tener datos frescos sin que el usuario tenga que pedirlo.
        if (_announcer.LastWeather is { } lastSnap)
        {
            Temperature = (decimal)Math.Clamp(lastSnap.Temperature, -30.0, 50.0);
            Humidity = (decimal)Math.Clamp(lastSnap.Humidity, 5.0, 100.0);
        }
        else if (_announcer.ActiveWeatherLocation is not null)
        {
            // Marcar como "primer fetch" para que OnWeatherUpdated no
            // dispare un anuncio automático sin pedido del usuario.
            _isFirstWeatherFetch = true;
            _ = Weather.FetchNowAsync();
        }

        UpdateClimateSummary();

        _clockTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Normal,
            OnClockTick);

        _clockTimer.Start();
        OnClockTick(this, EventArgs.Empty);

        // Tema
        IsDarkTheme = ThemeService.IsDarkVariant;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => IsDarkTheme = ThemeService.IsDarkVariant);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _announcer.SetInterfaceLanguage(VoiceLocalizationService.CurrentLanguage);
        var currentName = SelectedProfile?.Name;
        _suppressProfileApply = true;
        Profiles.Clear();
        foreach (var profile in _announcer.Profiles)
        {
            profile.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(profile);
        }
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == currentName) ?? Profiles.FirstOrDefault();
        _suppressProfileApply = false;
        ApplySelectedProfile();
        OnClockTick(this, EventArgs.Empty);
        StatusMessage = _announcer.IsInitialized
            ? VoiceLocalizationService.GetString("Loc.RMVoice.Status.BassReady")
            : VoiceLocalizationService.GetString("Loc.RMVoice.Status.BassUnavailable");
    }

    // ------------------------------------------------------------------ //
    //  Reloj
    // ------------------------------------------------------------------ //

    private void OnClockTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;

        CurrentTimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        try
        {
            var culture = VoiceLocalizationService.CurrentCulture;
            var dateFormat = culture.TwoLetterISOLanguageName == "en"
                ? "dddd, MMMM d, yyyy"
                : "dddd, d 'de' MMMM 'de' yyyy";
            CurrentDateText = now.ToString(dateFormat, culture);
        }
        catch
        {
            CurrentDateText = now.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
        }

        if (!AutoAnnounce) return;
        if (now.Minute == _lastAutoAnnouncedMinute) return;

        _lastAutoAnnouncedMinute = now.Minute;

        SelectedHour = Hours[now.Hour];
        SelectedMinute = Minutes[now.Minute];

        PlayAnnounce(AutoAnnounceMode, now.Hour, now.Minute);
    }

    partial void OnAutoAnnounceChanged(bool value) => _lastAutoAnnouncedMinute = -1;

    // ------------------------------------------------------------------ //
    //  Reproducción vía Engine
    // ------------------------------------------------------------------ //

    private void PlayAnnounce(AnnounceMode mode, int hour, int minute)
    {
        if (!_announcer.IsInitialized)
        {
            StatusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.EngineUnavailable");
            return;
        }

        var content = mode switch
        {
            AnnounceMode.HourMinute => AnnounceContent.HourAndMinute,
            AnnounceMode.ClimateOnly => AnnounceContent.ClimateOnly,
            _ => AnnounceContent.HourMinuteAndClimate
        };

        var req = new AnnounceRequest
        {
            Content = content,
            Hour = hour,
            Minute = minute,
            Temperature = (int)Math.Round(Temperature, MidpointRounding.AwayFromZero),
            Humidity = (int)Math.Round(Humidity, MidpointRounding.AwayFromZero),
            Volume = (float)(Volume / 100.0)
        };

        // Lanzar en background: Play es bloqueante.
        _ = Task.Run(() =>
        {
            try
            {
                _announcer.Play(req);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsPlaying = false;
                    StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.PlayError", ex.Message);
                });
            }
        });
    }

    private void OnPlaybackStarted(object? sender, AnnounceRequest req)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = true;
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.Playing", req.DisplayText);
        });
    }

    private void OnPlaybackCompleted(object? sender, AnnounceRequest req)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            StatusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.PlaybackFinished");
        });
    }

    private void OnPlaybackFailed(object? sender, Exception ex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.PlaybackError", ex.Message);
        });
    }

    // ------------------------------------------------------------------ //
    //  Comandos de reproducción
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private void PlayManual()
        => PlayAnnounce(ManualMode, SelectedHour?.Value ?? 0, SelectedMinute?.Value ?? 0);

    [RelayCommand]
    private void PlayCurrentTime()
    {
        var now = DateTime.Now;
        SelectedHour = Hours[now.Hour];
        SelectedMinute = Minutes[now.Minute];
        PlayAnnounce(AnnounceMode.HourMinute, now.Hour, now.Minute);
    }

    [RelayCommand]
    private void PlayCurrentClimate()
        => PlayAnnounce(AnnounceMode.ClimateOnly, 0, 0);

    [RelayCommand]
    private void PlayCurrentTimeAndClimate()
    {
        var now = DateTime.Now;
        SelectedHour = Hours[now.Hour];
        SelectedMinute = Minutes[now.Minute];
        PlayAnnounce(AnnounceMode.HourMinuteAndClimate, now.Hour, now.Minute);
    }

    [RelayCommand]
    private void Stop()
    {
        _announcer.Stop();
        IsPlaying = false;
        StatusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.Stopped");
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeService.Toggle();
        IsDarkTheme = ThemeService.IsDarkVariant;
    }

    partial void OnVolumeChanged(double value)
    {
        _announcer.Volume = (float)(value / 100.0);
        OnPropertyChanged(nameof(VolumeText));
    }

    partial void OnTemperatureChanged(decimal value) => UpdateClimateSummary();
    partial void OnHumidityChanged(decimal value) => UpdateClimateSummary();

    private void UpdateClimateSummary() => ClimateSummary = $"{Temperature:0.#} °C · {Humidity:0} %";

    // ------------------------------------------------------------------ //
    //  Integración con el panel de clima
    // ------------------------------------------------------------------ //

    private void OnWeatherUpdated(WeatherSnapshot snapshot)
    {
        Temperature = (decimal)Math.Clamp(snapshot.Temperature, -30.0, 50.0);
        Humidity = (decimal)Math.Clamp(snapshot.Humidity, 5.0, 100.0);

        StatusMessage =
            VoiceLocalizationService.Format("Loc.RMVoice.Status.WeatherUpdated",
                snapshot.Temperature,
                snapshot.Humidity,
                _announcer.ActiveWeatherLocation?.Name ?? "—");

        // Si el fetch fue del arranque (silencioso), no anunciar.
        bool isInitialFetch = _isFirstWeatherFetch;
        _isFirstWeatherFetch = false;

        if (Weather.AnnounceAfterFetch && !isInitialFetch)
        {
            var now = DateTime.Now;
            PlayAnnounce(AutoAnnounceMode, now.Hour, now.Minute);
        }
    }

    private void OnWeatherFetchFailed(string error)
        => StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.WeatherError", error);

    // ------------------------------------------------------------------ //
    //  Selección de archivos
    // ------------------------------------------------------------------ //

    [RelayCommand] private Task BrowseHourAudioAsync() => BrowseAudioAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Hours"), p => SelectedProfile!.HourAudioPath = p);
    [RelayCommand] private Task BrowseMinuteAudioAsync() => BrowseAudioAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Minutes"), p => SelectedProfile!.MinuteAudioPath = p);
    [RelayCommand] private Task BrowseTemperatureAudioAsync() => BrowseAudioAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Temperature"), p => SelectedProfile!.TemperatureAudioPath = p);
    [RelayCommand] private Task BrowseHumidityAudioAsync() => BrowseAudioAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Humidity"), p => SelectedProfile!.HumidityAudioPath = p);

    private async Task BrowseAudioAsync(string label, Action<string> assign)
    {
        if (SelectedProfile is null) return;
        var path = await _dialogs.PickFileAsync(VoiceLocalizationService.Format("Loc.RMVoice.Dialog.SelectAudio", label), AudioPatterns);
        if (path is null) return;
        SelectedProfile.UseInterfaceLanguageDefaults = false;
        assign(path);
        ApplySelectedProfile();
        PersistProfiles();
    }

    [RelayCommand] private Task BrowseHourTableAsync() => BrowseTableAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Hours"), p => SelectedProfile!.HourTablePath = p);
    [RelayCommand] private Task BrowseMinuteTableAsync() => BrowseTableAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Minutes"), p => SelectedProfile!.MinuteTablePath = p);
    [RelayCommand] private Task BrowseTemperatureTableAsync() => BrowseTableAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Temperature"), p => SelectedProfile!.TemperatureTablePath = p);
    [RelayCommand] private Task BrowseHumidityTableAsync() => BrowseTableAsync(VoiceLocalizationService.GetString("Loc.RMVoice.Unit.Humidity"), p => SelectedProfile!.HumidityTablePath = p);

    private async Task BrowseTableAsync(string label, Action<string> assign)
    {
        if (SelectedProfile is null) return;
        var path = await _dialogs.PickFileAsync(VoiceLocalizationService.Format("Loc.RMVoice.Dialog.SelectTable", label), TablePatterns);
        if (path is null) return;
        SelectedProfile.UseInterfaceLanguageDefaults = false;
        assign(path);
        ApplySelectedProfile();
        PersistProfiles();
    }

    // ------------------------------------------------------------------ //
    //  Perfiles
    // ------------------------------------------------------------------ //

    private void LoadProfiles()
    {
        _suppressProfileApply = true;

        foreach (var p in _announcer.Profiles)
        {
            p.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(p);
        }

        if (Profiles.Count == 0)
        {
            var fallback = new VoiceProfile { Name = VoiceLocalizationService.GetString("Loc.RMVoice.Profile.New") };
            fallback.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(fallback);
        }

        SelectedProfile = Profiles.FirstOrDefault();

        _suppressProfileApply = false;

        ApplySelectedProfile();
    }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
        if (_suppressProfileApply) return;
        ApplySelectedProfile();
    }

    private void OnProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VoiceProfile.HourAudioPath) or nameof(VoiceProfile.MinuteAudioPath)
            or nameof(VoiceProfile.TemperatureAudioPath) or nameof(VoiceProfile.HumidityAudioPath)
            or nameof(VoiceProfile.HourTablePath) or nameof(VoiceProfile.MinuteTablePath)
            or nameof(VoiceProfile.TemperatureTablePath) or nameof(VoiceProfile.HumidityTablePath))
        {
            if (sender is VoiceProfile profile)
                profile.UseInterfaceLanguageDefaults = false;
        }
    }

    [RelayCommand]
    private void ReloadProfile() => ApplySelectedProfile();

    [RelayCommand]
    private void SaveProfiles() => PersistProfiles();

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new VoiceProfile
        {
            Name = $"Perfil {Profiles.Count + 1}",
            Language = "es",
            Gender = "—"
        };

        profile.PropertyChanged += OnProfilePropertyChanged;
        Profiles.Add(profile);
        SelectedProfile = profile;
        PersistProfiles();
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;
        var source = SelectedProfile;

        var copy = new VoiceProfile
        {
            Name = source.Name + " (copia)",
            Language = source.Language,
            Gender = source.Gender,
            HourAudioPath = source.HourAudioPath,
            MinuteAudioPath = source.MinuteAudioPath,
            TemperatureAudioPath = source.TemperatureAudioPath,
            HumidityAudioPath = source.HumidityAudioPath,
            HourTablePath = source.HourTablePath,
            MinuteTablePath = source.MinuteTablePath,
            TemperatureTablePath = source.TemperatureTablePath,
            HumidityTablePath = source.HumidityTablePath,
            UseInterfaceLanguageDefaults = source.UseInterfaceLanguageDefaults
        };

        copy.PropertyChanged += OnProfilePropertyChanged;
        Profiles.Add(copy);
        SelectedProfile = copy;
        PersistProfiles();
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is null) return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        PersistProfiles();
    }

    private void PersistProfiles()
    {
        _profileStore.Save(Profiles);
        _announcer.ReloadProfiles();

        // Refrescar colección local con lo que el Engine leyó.
        _suppressProfileApply = true;
        var currentName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var p in _announcer.Profiles) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == currentName)
                          ?? Profiles.FirstOrDefault();
        _suppressProfileApply = false;

        StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.ProfilesSaved", _profileStore.FilePath);
    }

    // ------------------------------------------------------------------ //
    //  Aplicar perfil → el Engine carga streams, ViewModel carga regiones
    // ------------------------------------------------------------------ //

    private void ApplySelectedProfile()
    {
        HourRegions.Clear();
        MinuteRegions.Clear();
        TemperatureRegions.Clear();
        HumidityRegions.Clear();

        var profile = SelectedProfile;

        // El Engine carga audios y regiones internamente.
        _announcer.ActiveProfile = profile;

        if (profile is null)
        {
            StatusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.NoProfile");
            return;
        }

        // Copia local de regiones para edición.
        LoadRegions(HourRegions, profile.HourTablePath);
        LoadRegions(MinuteRegions, profile.MinuteTablePath);
        LoadRegions(TemperatureRegions, profile.TemperatureTablePath);
        LoadRegions(HumidityRegions, profile.HumidityTablePath);

        StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.ProfileLoaded",
            profile.Name, HourRegions.Count, MinuteRegions.Count, TemperatureRegions.Count, HumidityRegions.Count);
    }

    private static void LoadRegions(ObservableCollection<AudioRegion> target, string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                foreach (var r in RegionTableParser.Parse(path))
                    target.Add(r);
            }
        }
        catch
        {
            // Silencioso: tabla inválida no debería romper la UI.
        }
    }

    // ------------------------------------------------------------------ //
    //  Edición de tablas
    // ------------------------------------------------------------------ //

    [RelayCommand] private Task SaveHourTableAsync() => SaveTableAsync(HourRegions, "H_region_table.txt", isHour: true);
    [RelayCommand] private Task SaveHourTableAsAsync() => SaveTableAsAsync(HourRegions, "H_region_table.txt", isHour: true);
    [RelayCommand] private void ReloadHourTable() => ReloadTable(HourRegions, isHour: true);

    [RelayCommand] private Task SaveMinuteTableAsync() => SaveTableAsync(MinuteRegions, "M_region_table.txt");
    [RelayCommand] private Task SaveMinuteTableAsAsync() => SaveTableAsAsync(MinuteRegions, "M_region_table.txt");
    [RelayCommand] private void ReloadMinuteTable() => ReloadTable(MinuteRegions);

    [RelayCommand] private Task SaveTemperatureTableAsync() => SaveTableAsync(TemperatureRegions, "T_region_table.txt", isTemperature: true);
    [RelayCommand] private Task SaveTemperatureTableAsAsync() => SaveTableAsAsync(TemperatureRegions, "T_region_table.txt", isTemperature: true);
    [RelayCommand] private void ReloadTemperatureTable() => ReloadTable(TemperatureRegions, isTemperature: true);

    [RelayCommand] private Task SaveHumidityTableAsync() => SaveTableAsync(HumidityRegions, "Hu_region_table.txt", isHumidity: true);
    [RelayCommand] private Task SaveHumidityTableAsAsync() => SaveTableAsAsync(HumidityRegions, "Hu_region_table.txt", isHumidity: true);
    [RelayCommand] private void ReloadHumidityTable() => ReloadTable(HumidityRegions, isHumidity: true);

    private async Task SaveTableAsync(ObservableCollection<AudioRegion> regions, string suggestedName,
        bool isHour = false, bool isTemperature = false, bool isHumidity = false)
    {
        if (SelectedProfile is null) return;

        var path = GetTablePath(isHour, isTemperature, isHumidity);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = await _dialogs.SaveFileAsync(VoiceLocalizationService.Format("Loc.RMVoice.Dialog.SaveTable", suggestedName), suggestedName, TablePatterns);
            if (path is null) return;
            SetTablePath(isHour, isTemperature, isHumidity, path);
        }

        try
        {
            RegionTableParser.Save(path, regions);
            _announcer.ReloadActiveProfile();
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableSaved", path);
            PersistProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableSaveError", ex.Message);
        }
    }

    private async Task SaveTableAsAsync(ObservableCollection<AudioRegion> regions, string suggestedName,
        bool isHour = false, bool isTemperature = false, bool isHumidity = false)
    {
        if (SelectedProfile is null) return;

        var path = await _dialogs.SaveFileAsync(VoiceLocalizationService.Format("Loc.RMVoice.Dialog.SaveTableAs", suggestedName), suggestedName, TablePatterns);
        if (path is null) return;

        try
        {
            RegionTableParser.Save(path, regions);
            SetTablePath(isHour, isTemperature, isHumidity, path);
            _announcer.ReloadActiveProfile();
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableSaved", path);
            PersistProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableSaveError", ex.Message);
        }
    }

    private void ReloadTable(ObservableCollection<AudioRegion> target,
        bool isHour = false, bool isTemperature = false, bool isHumidity = false)
    {
        if (SelectedProfile is null) return;

        var path = GetTablePath(isHour, isTemperature, isHumidity);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = VoiceLocalizationService.GetString("Loc.RMVoice.Status.NoTableToReload");
            return;
        }

        try
        {
            var parsed = RegionTableParser.Parse(path);
            target.Clear();
            foreach (var r in parsed) target.Add(r);
            _announcer.ReloadActiveProfile();
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableReloaded", target.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = VoiceLocalizationService.Format("Loc.RMVoice.Status.TableReloadError", ex.Message);
        }
    }

    private string GetTablePath(bool isHour, bool isTemperature, bool isHumidity)
    {
        var p = SelectedProfile!;
        if (isHour) return p.HourTablePath;
        if (isTemperature) return p.TemperatureTablePath;
        if (isHumidity) return p.HumidityTablePath;
        return p.MinuteTablePath;
    }

    private void SetTablePath(bool isHour, bool isTemperature, bool isHumidity, string path)
    {
        var p = SelectedProfile!;
        p.UseInterfaceLanguageDefaults = false;
        if (isHour) { p.HourTablePath = path; return; }
        if (isTemperature) { p.TemperatureTablePath = path; return; }
        if (isHumidity) { p.HumidityTablePath = path; return; }
        p.MinuteTablePath = path;
    }

    // ------------------------------------------------------------------ //

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { ThemeService.ThemeChanged -= OnThemeChanged; } catch { }
        VoiceLocalizationService.LanguageChanged -= OnLanguageChanged;
        _clockTimer.Stop();

        _announcer.PlaybackStarted -= OnPlaybackStarted;
        _announcer.PlaybackCompleted -= OnPlaybackCompleted;
        _announcer.PlaybackFailed -= OnPlaybackFailed;

        Weather.WeatherUpdated -= OnWeatherUpdated;
        Weather.FetchFailed -= OnWeatherFetchFailed;
        Weather.Dispose();
    }
}
