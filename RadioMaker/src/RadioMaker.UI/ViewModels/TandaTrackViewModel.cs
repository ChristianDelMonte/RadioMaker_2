using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Wrapper de UI sobre <see cref="TandaTrack"/>.
/// Al cambiar el FilePath lee la duracion del archivo automaticamente.
/// -------------
public sealed partial class TandaTrackViewModel : ViewModelBase
{
    private readonly TandaTrack _track;

    public TandaTrack Source => _track;

    /// -------------Se dispara cuando cambia cualquier propiedad relevante.</summary>
    public event EventHandler? Changed;

    // ───── Propiedades observables ───────────────────────────────────────

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _volumeDb;
    [ObservableProperty] private double _delayBeforeSeconds;
    [ObservableProperty] private string _notes;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public double DurationSeconds => _track.DurationSeconds;

    public string DurationText => $"Duracion: {AudioFileInfoReader.FormatDuration(DurationSeconds)}";

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : (!string.IsNullOrWhiteSpace(FilePath)
                ? Path.GetFileName(FilePath)
                : "(sin archivo)");

    public string VolumeText => VolumeDb == 0.0
        ? "0.0 dB"
        : VolumeDb > 0
            ? $"+{VolumeDb:F1} dB"
            : $"{VolumeDb:F1} dB";

    public string DelayText => DelayBeforeSeconds <= 0.05
        ? "Sin espera"
        : $"Espera {DelayBeforeSeconds:F1} s";

    // ===== INTEGRACIÓN RMWave =====
    public bool IsRmwProject =>
        !string.IsNullOrWhiteSpace(FilePath)
        && FilePath.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);

    public string FileTypeBadge => IsRmwProject ? "PROYECTO RMWAVE" : string.Empty;
    public string FileTypeBadgeColor => IsRmwProject ? "Accent" : "Transparent";

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    // ───── Constructor ───────────────────────────────────────────────────

    public TandaTrackViewModel(TandaTrack track)
    {
        _track = track;

        _filePath = track.FilePath;
        _title = track.Title;
        _isEnabled = track.IsEnabled;
        _volumeDb = track.VolumeDb;
        _delayBeforeSeconds = track.DelayBeforeSeconds;
        _notes = track.Notes;

        // Si el modelo no tiene duracion pero hay path, la leemos en background.
        if (track.DurationSeconds <= 0 && !string.IsNullOrWhiteSpace(track.FilePath))
            _ = RefreshDurationAsync(track.FilePath);
    }

    // ───── Sincronizacion con el modelo ──────────────────────────────────

    public void ApplyToModel()
    {
        _track.FilePath = FilePath ?? string.Empty;
        _track.Title = Title ?? string.Empty;
        _track.IsEnabled = IsEnabled;
        _track.VolumeDb = VolumeDb;
        _track.DelayBeforeSeconds = DelayBeforeSeconds;
        _track.Notes = Notes ?? string.Empty;
    }

    // ───── Lectura de duracion ───────────────────────────────────────────

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
        NotifyChanged();
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnFilePathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(HasFile));

        // Reset + recalcular duracion.
        _track.DurationSeconds = 0;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));

        OnPropertyChanged(nameof(IsRmwProject));
        OnPropertyChanged(nameof(FileTypeBadge));
        OnPropertyChanged(nameof(FileTypeBadgeColor));

        if (!string.IsNullOrWhiteSpace(value))
            _ = RefreshDurationAsync(value);

        NotifyChanged();
    }

    partial void OnTitleChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value) { ApplyToModel(); NotifyChanged(); }

    partial void OnVolumeDbChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(VolumeText));
        NotifyChanged();
    }

    partial void OnDelayBeforeSecondsChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DelayText));
        NotifyChanged();
    }

    partial void OnNotesChanged(string value) { ApplyToModel(); NotifyChanged(); }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}