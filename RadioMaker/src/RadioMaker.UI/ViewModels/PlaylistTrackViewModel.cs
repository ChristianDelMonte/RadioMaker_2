using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

public sealed partial class PlaylistTrackViewModel : ViewModelBase, IDisposable
{
    private readonly PlaylistTrack _track;

    private readonly EventHandler _themeChangedHandler;
    private bool _disposed;

    public PlaylistTrack Source => _track;
    public event EventHandler? Changed;

    // ───── Propiedades observables ───────────────────────────────────────

    [ObservableProperty] private PlaylistItemKind _itemKind;
    [ObservableProperty] private string _filePath;
    [ObservableProperty] private LocutionCommand _locutionCommand = LocutionCommand.HourAndMinute;
    [ObservableProperty] private double _estimatedDurationSeconds = 5.0;
    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _volumeDb;

    [ObservableProperty] private TrackMixMode _mixMode;
    [ObservableProperty] private string _mixAudioPath;
    [ObservableProperty] private LocutionCommand _mixLocutionCommand = LocutionCommand.HourAndMinute;
    [ObservableProperty] private double _mixStartSeconds;
    [ObservableProperty] private double _mixVolumeDb;
    [ObservableProperty] private double _mixDuckLevel;

    [ObservableProperty] private string _notes;

    [ObservableProperty] private bool _isPlayed;
    [ObservableProperty] private bool _isCurrentlyPlaying;

    // ───── Opciones para ComboBoxes ──────────────────────────────────────

    public LocutionCommand[] AvailableLocutionCommands { get; } =
    {
        LocutionCommand.HourAndMinute,
        LocutionCommand.Climate,
        LocutionCommand.HourAndClimate
    };

    // ───── Derivadas ─────────────────────────────────────────────────────

    public double DurationSeconds => _track.DurationSeconds;

    public string DurationText
    {
        get
        {
            if (IsLocutionItem)
            {
                return EstimatedDurationSeconds > 0.5
                    ? $"Duración estimada: {AudioFileInfoReader.FormatDuration(EstimatedDurationSeconds)}"
                    : "Locución (sin duración estimada)";
            }

            return $"Duracion: {AudioFileInfoReader.FormatDuration(DurationSeconds)}";
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (IsLocutionItem)
            {
                var cmdText = LocutionCommand switch
                {
                    LocutionCommand.HourAndMinute => "Hora + minutos",
                    LocutionCommand.Climate => "Clima",
                    LocutionCommand.HourAndClimate => "Hora + clima",
                    _ => "Locución"
                };

                return !string.IsNullOrWhiteSpace(Title)
                    ? Title
                    : $"Locución: {cmdText}";
            }

            return !string.IsNullOrWhiteSpace(Title)
                ? Title
                : (!string.IsNullOrWhiteSpace(FilePath)
                    ? Path.GetFileName(FilePath)
                    : "(sin archivo)");
        }
    }

    public string VolumeText => VolumeDb == 0.0
        ? "0.0 dB"
        : VolumeDb > 0
            ? $"+{VolumeDb:F1} dB"
            : $"{VolumeDb:F1} dB";

    public string MixStartText
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, MixStartSeconds));
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }
    }

    public string MixVolumeText => $"{MixVolumeDb:F1} dB";
    public string MixDuckLevelText => $"{MixDuckLevel:P0}";

    // ───── Flags de UI ──────────────────────────────────────────────────

    public bool IsAudioItem => ItemKind == PlaylistItemKind.Audio;
    public bool IsLocutionItem => ItemKind == PlaylistItemKind.Locution;

    /// -------------Indice numerico del ItemKind para bindear ComboBox.</summary>
    public int ItemKindIndex
    {
        get => (int)ItemKind;
        set
        {
            if (value < 0 || value > 1) return;
            var k = (PlaylistItemKind)value;
            if (ItemKind == k) return;
            ItemKind = k;
        }
    }

    /// -------------Indice numerico del MixMode para bindear ComboBox.</summary>
    public int MixModeIndex
    {
        get => (int)MixMode;
        set
        {
            if (value < 0 || value > 2) return;
            var m = (TrackMixMode)value;
            if (MixMode == m) return;
            MixMode = m;
        }
    }

    /// -------------True si el mix está activo (audio con archivo, o locución).</summary>
    public bool HasMix => MixMode switch
    {
        TrackMixMode.Audio => !string.IsNullOrWhiteSpace(MixAudioPath),
        TrackMixMode.Locution => true,
        _ => false
    };

    public bool HasNoMix => !HasMix;

    public bool IsMixAudio => MixMode == TrackMixMode.Audio;
    public bool IsMixLocution => MixMode == TrackMixMode.Locution;
    public bool IsMixActive => IsMixAudio || IsMixLocution;

    /// -------------El mix solo se puede configurar si el ítem es de audio.</summary>
    public bool CanHaveMix => IsAudioItem;

    public string MixDisplayName => MixMode switch
    {
        TrackMixMode.Audio => !string.IsNullOrWhiteSpace(MixAudioPath)
            ? Path.GetFileName(MixAudioPath)
            : "(sin mixado)",
        TrackMixMode.Locution => MixLocutionCommand switch
        {
            LocutionCommand.HourAndMinute => "Loc: Hora + minutos",
            LocutionCommand.Climate => "Loc: Clima",
            LocutionCommand.HourAndClimate => "Loc: Hora + clima",
            _ => "Locución"
        },
        _ => "(sin mixado)"
    };

    public double MaxMixStartSeconds => DurationSeconds > 0.5 ? DurationSeconds - 0.5 : 0.0;

    // ===== INTEGRACIÓN RMWave =====
    /// -------------True si el archivo es un proyecto .rmw.</summary>
    public bool IsRmwProject =>
        !string.IsNullOrWhiteSpace(FilePath)
        && FilePath.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);

    /// -------------Etiqueta a mostrar cuando es .rmw (vacío para otros).</summary>
    public string FileTypeBadge => IsRmwProject ? "PROYECTO RMWAVE" : string.Empty;

    /// -------------Color de la etiqueta (clave de recurso).</summary>
    public string FileTypeBadgeColor => IsRmwProject ? "Accent" : "Transparent";

    public string MarkerColor
    {
        get
        {
            if (IsCurrentlyPlaying) return "Error";
            if (IsPlayed) return "FgSubtle";
            if (IsLocutionItem) return "Accent";
            return HasMix ? "TrackMixColor" : "Accent";
        }
    }

    public double EffectiveOpacity
    {
        get
        {
            if (IsCurrentlyPlaying) return 1.0;
            if (IsPlayed) return 0.35;
            return IsEnabled ? 1.0 : 0.5;
        }
    }

    public string RowBackground
    {
        get
        {
            if (IsCurrentlyPlaying) return "RowPlayingBg";
            if (IsPlayed) return "RowPlayedBg";
            return "Transparent";
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaylistTrackViewModel(PlaylistTrack track)
    {
        _track = track;

        _itemKind = track.ItemKind;
        _filePath = track.FilePath;
        _locutionCommand = track.LocutionCommand;
        _estimatedDurationSeconds = track.EstimatedDurationSeconds;
        _title = track.Title;
        _isEnabled = track.IsEnabled;
        _volumeDb = track.VolumeDb;

        _mixMode = DetermineMixMode(track);
        _mixAudioPath = track.MixAudioPath;
        _mixLocutionCommand = track.MixLocutionCommand;
        _mixStartSeconds = track.MixStartSeconds;
        _mixVolumeDb = track.MixVolumeDb;
        _mixDuckLevel = track.MixDuckLevel;

        _notes = track.Notes;

        if (track.ItemKind == PlaylistItemKind.Audio &&
            track.DurationSeconds <= 0 &&
            !string.IsNullOrWhiteSpace(track.FilePath))
        {
            _ = RefreshDurationAsync(track.FilePath);
        }

        _themeChangedHandler = OnThemeChanged;
        RadioMaker.UI.Services.ThemeService.ThemeChanged += _themeChangedHandler;
    }

    private static TrackMixMode DetermineMixMode(PlaylistTrack t)
    {
        if (t.MixContentType == MixContentType.Locution)
            return TrackMixMode.Locution;

        if (string.IsNullOrWhiteSpace(t.MixAudioPath))
            return TrackMixMode.None;

        return TrackMixMode.Audio;
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(MarkerColor));
            OnPropertyChanged(nameof(RowBackground));
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { RadioMaker.UI.Services.ThemeService.ThemeChanged -= _themeChangedHandler; }
        catch { }
    }

    public void ApplyToModel()
    {
        _track.ItemKind = ItemKind;
        _track.FilePath = FilePath ?? string.Empty;
        _track.LocutionCommand = LocutionCommand;
        _track.EstimatedDurationSeconds = Math.Max(0.5, EstimatedDurationSeconds);
        _track.Title = Title ?? string.Empty;
        _track.IsEnabled = IsEnabled;
        _track.VolumeDb = VolumeDb;
        _track.Notes = Notes ?? string.Empty;

        // El mix solo aplica a items de audio.
        if (ItemKind == PlaylistItemKind.Locution)
        {
            _track.MixContentType = MixContentType.Audio;
            _track.MixAudioPath = string.Empty;
            _track.MixStartSeconds = 0;
            _track.MixVolumeDb = 0;
            _track.MixDuckLevel = 1.0;
            return;
        }

        switch (MixMode)
        {
            case TrackMixMode.None:
                _track.MixContentType = MixContentType.Audio;
                _track.MixAudioPath = string.Empty;
                break;

            case TrackMixMode.Audio:
                _track.MixContentType = MixContentType.Audio;
                _track.MixAudioPath = MixAudioPath ?? string.Empty;
                break;

            case TrackMixMode.Locution:
                _track.MixContentType = MixContentType.Locution;
                _track.MixLocutionCommand = MixLocutionCommand;
                _track.MixAudioPath = string.Empty;
                break;
        }

        _track.MixStartSeconds = MixStartSeconds;
        _track.MixVolumeDb = MixVolumeDb;
        _track.MixDuckLevel = MixDuckLevel;
    }

    public void CopyFrom(PlaylistTrackViewModel other)
    {
        if (other is null) return;

        ItemKind = other.ItemKind;
        FilePath = other.FilePath;
        LocutionCommand = other.LocutionCommand;
        EstimatedDurationSeconds = other.EstimatedDurationSeconds;
        Title = other.Title;
        IsEnabled = other.IsEnabled;
        VolumeDb = other.VolumeDb;

        MixMode = other.MixMode;
        MixAudioPath = other.MixAudioPath;
        MixLocutionCommand = other.MixLocutionCommand;
        MixStartSeconds = other.MixStartSeconds;
        MixVolumeDb = other.MixVolumeDb;
        MixDuckLevel = other.MixDuckLevel;

        Notes = other.Notes;

        _track.DurationSeconds = other.Source.DurationSeconds;

        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(MaxMixStartSeconds));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(HasMix));
        OnPropertyChanged(nameof(HasNoMix));
        OnPropertyChanged(nameof(MixDisplayName));
        OnPropertyChanged(nameof(MarkerColor));
        NotifyChanged();
    }

    private async Task RefreshDurationAsync(string path)
    {
        double seconds = 0;
        try
        {
            seconds = await Task.Run(() => AudioFileInfoReader.GetDurationSeconds(path));
        }
        catch { }

        if (!string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase)) return;

        _track.DurationSeconds = seconds;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(MaxMixStartSeconds));
        NotifyChanged();
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnIsPlayedChanged(bool value)
    {
        OnPropertyChanged(nameof(MarkerColor));
        OnPropertyChanged(nameof(EffectiveOpacity));
        OnPropertyChanged(nameof(RowBackground));
    }

    partial void OnIsCurrentlyPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(MarkerColor));
        OnPropertyChanged(nameof(EffectiveOpacity));
        OnPropertyChanged(nameof(RowBackground));
    }

    partial void OnItemKindChanged(PlaylistItemKind value)
    {
        // Resetear el mix al cambiar de tipo (una locución no tiene mix).
        if (value == PlaylistItemKind.Locution)
        {
            MixMode = TrackMixMode.None;
            MixAudioPath = string.Empty;

            // Al entrar a modo Locución, rellenar el título con el
            // auto-generado según el comando actual. Si el usuario tenía
            // un título custom previo (por ejemplo de cuando era Audio),
            // se sobreescribe porque el tipo cambió explícitamente.
            //
            // Nota: si el usuario está DENTRO de Locución y solo cambia
            // el comando, ese caso lo maneja OnLocutionCommandChanged
            // (que respeta títulos custom).
            Title = BuildLocutionTitle(LocutionCommand);
        }

        ApplyToModel();

        OnPropertyChanged(nameof(ItemKindIndex));
        OnPropertyChanged(nameof(IsAudioItem));
        OnPropertyChanged(nameof(IsLocutionItem));
        OnPropertyChanged(nameof(CanHaveMix));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(MarkerColor));
        NotifyChanged();
    }

    partial void OnLocutionCommandChanged(LocutionCommand value)
    {
        // Actualizar el título automáticamente si era auto-generado
        // (o estaba vacío). Si el usuario escribió un título custom,
        // se respeta y no se toca.
        if (IsLocutionItem && IsLocutionTitleAutoGenerated())
        {
            // Asignar Title dispara OnTitleChanged → ApplyToModel().
            Title = BuildLocutionTitle(value);
        }
        else
        {
            ApplyToModel();
        }

        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnEstimatedDurationSecondsChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DurationText));
        NotifyChanged();
    }

    partial void OnFilePathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));

        _track.DurationSeconds = 0;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(MaxMixStartSeconds));

        // ===== INTEGRACIÓN RMWave =====
        OnPropertyChanged(nameof(IsRmwProject));
        OnPropertyChanged(nameof(FileTypeBadge));
        OnPropertyChanged(nameof(FileTypeBadgeColor));

        if (!string.IsNullOrWhiteSpace(value) && IsAudioItem)
            _ = RefreshDurationAsync(value);

        NotifyChanged();
    }

    partial void OnTitleChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(EffectiveOpacity));
        NotifyChanged();
    }

    partial void OnVolumeDbChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(VolumeText));
        NotifyChanged();
    }

    partial void OnMixModeChanged(TrackMixMode value)
    {
        ApplyToModel();

        OnPropertyChanged(nameof(MixModeIndex));
        OnPropertyChanged(nameof(IsMixAudio));
        OnPropertyChanged(nameof(IsMixLocution));
        OnPropertyChanged(nameof(IsMixActive));
        OnPropertyChanged(nameof(HasMix));
        OnPropertyChanged(nameof(HasNoMix));
        OnPropertyChanged(nameof(MixDisplayName));
        OnPropertyChanged(nameof(MarkerColor));
        NotifyChanged();
    }

    partial void OnMixAudioPathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(HasMix));
        OnPropertyChanged(nameof(HasNoMix));
        OnPropertyChanged(nameof(MixDisplayName));
        OnPropertyChanged(nameof(MarkerColor));
        NotifyChanged();
    }

    partial void OnMixLocutionCommandChanged(LocutionCommand value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(MixDisplayName));
        NotifyChanged();
    }

    partial void OnMixStartSecondsChanged(double value)
    {
        double maxAllowed = MaxMixStartSeconds;
        double clamped = Math.Max(0, value);

        if (maxAllowed > 0 && clamped > maxAllowed)
            clamped = maxAllowed;

        if (Math.Abs(clamped - value) > 0.001)
        {
            MixStartSeconds = clamped;
            return;
        }

        ApplyToModel();
        OnPropertyChanged(nameof(MixStartText));
        NotifyChanged();
    }

    partial void OnMixVolumeDbChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(MixVolumeText));
        NotifyChanged();
    }

    partial void OnMixDuckLevelChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(MixDuckLevelText));
        NotifyChanged();
    }

    partial void OnNotesChanged(string value) { ApplyToModel(); NotifyChanged(); }

    /// -------------
    /// Devuelve el titulo auto-generado para un comando de locución.
    /// -------------
    private static string BuildLocutionTitle(LocutionCommand cmd) => cmd switch
    {
        LocutionCommand.HourAndMinute => "LOCUCION HORA",
        LocutionCommand.Climate => "LOCUCION TEMPERATURA",
        LocutionCommand.HourAndClimate => "LOCUCION HORA Y TEMPERATURA",
        _ => "LOCUCION"
    };

    /// -------------
    /// True si el Title actual coincide con alguno de los titulos
    /// auto-generados por locución (o está vacío). Sirve para decidir
    /// si conviene sobreescribirlo cuando el usuario cambia el comando.
    /// Un título custom del usuario NO se toca.
    /// -------------
    private bool IsLocutionTitleAutoGenerated()
    {
        if (string.IsNullOrWhiteSpace(Title)) return true;

        return Title == BuildLocutionTitle(LocutionCommand.HourAndMinute)
            || Title == BuildLocutionTitle(LocutionCommand.Climate)
            || Title == BuildLocutionTitle(LocutionCommand.HourAndClimate);
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}