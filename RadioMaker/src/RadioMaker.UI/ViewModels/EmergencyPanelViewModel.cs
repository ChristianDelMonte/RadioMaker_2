using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Scheduling;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

public sealed partial class EmergencyPanelViewModel : ViewModelBase
{
    private readonly EmergencySettings _settings;
    private readonly EmergencyWatchdogService? _service;

    public Action<EmergencySettings>? SettingsChanged { get; set; }

    // ───── Propiedades ───────────────────────────────────────────────────

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set { if (_settings.IsEnabled == value) return; _settings.IsEnabled = value; Save(); OnPropertyChanged(); }
    }

    public int SilenceThresholdSeconds
    {
        get => _settings.SilenceThresholdSeconds;
        set { if (_settings.SilenceThresholdSeconds == value) return; _settings.SilenceThresholdSeconds = value; Save(); OnPropertyChanged(); }
    }

    public double ThresholdDb
    {
        get => _settings.ThresholdDb;
        set { if (Math.Abs(_settings.ThresholdDb - value) < 0.1) return; _settings.ThresholdDb = value; Save(); OnPropertyChanged(); }
    }

    public bool DetectNoChannels
    {
        get => _settings.DetectNoChannels;
        set { if (_settings.DetectNoChannels == value) return; _settings.DetectNoChannels = value; Save(); OnPropertyChanged(); }
    }

    public bool DetectSilence
    {
        get => _settings.DetectSilence;
        set { if (_settings.DetectSilence == value) return; _settings.DetectSilence = value; Save(); OnPropertyChanged(); }
    }

    public EmergencySourceMode Mode
    {
        get => _settings.Mode;
        set { if (_settings.Mode == value) return; _settings.Mode = value; Save(); OnPropertyChanged(); OnPropertyChanged(nameof(IsPrearmed)); OnPropertyChanged(nameof(IsFolder)); }
    }

    public bool IsPrearmed => Mode == EmergencySourceMode.Prearmed || Mode == EmergencySourceMode.Hybrid;
    public bool IsFolder => Mode == EmergencySourceMode.Folder || Mode == EmergencySourceMode.Hybrid;

    public string PrearmedPlaylistPath
    {
        get => _settings.PrearmedPlaylistPath;
        set { if (_settings.PrearmedPlaylistPath == value) return; _settings.PrearmedPlaylistPath = value ?? ""; Save(); OnPropertyChanged(); }
    }

    public string EmergencyFolder
    {
        get => _settings.EmergencyFolder;
        set { if (_settings.EmergencyFolder == value) return; _settings.EmergencyFolder = value ?? ""; Save(); OnPropertyChanged(); }
    }

    public bool IncludeSubfolders
    {
        get => _settings.IncludeSubfolders;
        set { if (_settings.IncludeSubfolders == value) return; _settings.IncludeSubfolders = value; Save(); OnPropertyChanged(); }
    }

    public int FadeInMs
    {
        get => _settings.FadeInMs;
        set { if (_settings.FadeInMs == value) return; _settings.FadeInMs = value; Save(); OnPropertyChanged(); }
    }

    public int FadeOutMs
    {
        get => _settings.FadeOutMs;
        set { if (_settings.FadeOutMs == value) return; _settings.FadeOutMs = value; Save(); OnPropertyChanged(); }
    }

    public bool BeepEnabled
    {
        get => _settings.BeepEnabled;
        set { if (_settings.BeepEnabled == value) return; _settings.BeepEnabled = value; Save(); OnPropertyChanged(); }
    }

    public int BeepEveryNTracks
    {
        get => _settings.BeepEveryNTracks;
        set { if (_settings.BeepEveryNTracks == value) return; _settings.BeepEveryNTracks = value; Save(); OnPropertyChanged(); }
    }

    // ───── Estado en vivo ────────────────────────────────────────────────

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _statusMessage = "";

    public EmergencySourceMode[] AvailableModes { get; } =
    {
        EmergencySourceMode.Prearmed,
        EmergencySourceMode.Folder,
        EmergencySourceMode.Hybrid
    };

    public string LiveStatusText => IsActive
        ? (_service?.CurrentTrackTitle ?? "") + " — pista " + _service?.CurrentTrackIndex + " de " + _service?.CurrentTrackTotal + " en " + _service?.CurrentBusDisplayName
        : "";

    public EmergencyPanelViewModel(EmergencySettings settings, EmergencyWatchdogService? service = null)
    {
        _settings = settings ?? new EmergencySettings();
        _service = service;

        if (_service is not null)
        {
            _service.EmergencyStarted += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OnEmergencyStateChanged);
            _service.EmergencyStopped += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OnEmergencyStateChanged);
            _service.NowPlayingChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OnNowPlayingChanged);
            _service.LogMessage += (_, msg) => Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = msg);
        }
    }

    private void OnEmergencyStateChanged()
    {
        IsActive = _service?.IsActive ?? false;
        OnPropertyChanged(nameof(LiveStatusText));
        StopEmergencyCommand.NotifyCanExecuteChanged();
    }

    private void OnNowPlayingChanged()
    {
        // Re-notificar la propiedad compuesta que bindea el panel.
        // LiveStatusText lee en vivo CurrentTrackTitle/Index/Total del service.
        OnPropertyChanged(nameof(LiveStatusText));
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private void TestNow()
    {
        try { _service?.TriggerNow(); } catch { }
    }

    [RelayCommand(CanExecute = nameof(CanStopEmergency))]
    private void StopEmergency()
    {
        try { _service?.StopEmergency(); } catch { }
    }

    private bool CanStopEmergency() => _service?.IsActive ?? false;

    [RelayCommand]
    private void TestBeep()
    {
        try
        {
            var bus = _service is null ? null : (object?)_service.GetType()
                .GetField("_engine", System.Reflection.BindingFlags.NonPublic
                                    | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_service);

            // Acceso directo via service (que ya tiene el engine).
            _service?.TestBeepNow();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.Emergency.Status.Error"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task BrowsePrearmedPlaylistAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
            ? dt.MainWindow : null;
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = LocalizationService.GetString("Loc.Dialog.SelectEmergencyPlaylist.Title"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Playlist .rmf")
                    { Patterns = new[] { "*.rmf" } },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) PrearmedPlaylistPath = path;
        }
        catch { }
    }

    [RelayCommand]
    private async Task BrowseEmergencyFolderAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
            ? dt.MainWindow : null;
        if (topLevel is null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = LocalizationService.GetString("Loc.Dialog.SelectEmergencyFolder.Title"),
                    AllowMultiple = false
                });

            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) EmergencyFolder = path;
        }
        catch { }
    }

    // ───── Persistencia ──────────────────────────────────────────────────

    private void Save()
    {
        EmergencySettingsStore.Save(_settings);
        try { SettingsChanged?.Invoke(_settings); } catch { }
    }
}
