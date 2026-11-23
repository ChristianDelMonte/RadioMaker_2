using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

public sealed class AutoDjClockPreviewItem
{
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string GenreSlot { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public string DurationText => AudioFileInfoReader.FormatDuration(DurationSeconds);
}

public sealed partial class AutoDjClockWindowViewModel : ViewModelBase
{
    private readonly string[] _supportedExtensions;
    private readonly Func<AutoDjClock, IReadOnlyList<AutoDjClockTrack>, Task<bool>>? _takeAir;
    private readonly Action<AutoDjClock, IReadOnlyList<AutoDjClockTrack>>? _previewReady;
    private CancellationTokenSource? _scanCts;
    private IReadOnlyList<AutoDjClockTrack> _previewTracks = Array.Empty<AutoDjClockTrack>();

    [ObservableProperty] private string _sourceFolder = string.Empty;
    [ObservableProperty] private bool _includeSubfolders = true;
    [ObservableProperty] private int _targetDurationMinutes = 60;
    [ObservableProperty] private int _minTracksBetweenSameArtist = 3;
    [ObservableProperty] private string _clockSequenceText = "*";
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private double _generationProgress;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _generationProgressText = string.Empty;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _statusMessage = "Elige la carpeta musical y define el orden de categorías.";
    [ObservableProperty] private string _previewSummaryText = string.Empty;
    [ObservableProperty] private string? _currentClockPath;
    [ObservableProperty] private bool _isTakingAir;
    [ObservableProperty] private bool _takeAirAccepted;

    public ObservableCollection<AutoDjClockPreviewItem> PreviewTracks { get; } = new();

    public bool CanGenerate =>
        !IsGenerating && Directory.Exists(SourceFolder) && ParseSlots().Count > 0;

    public bool CanSavePlaylist => HasPreview && PreviewTracks.Count > 0;
    public bool CanTakeAir => HasPreview && PreviewTracks.Count > 0 && !IsTakingAir && _takeAir is not null;
    public bool HasCurrentClock => !string.IsNullOrWhiteSpace(CurrentClockPath);

    public AutoDjClockWindowViewModel(
        IEnumerable<string>? supportedExtensions = null,
        Func<AutoDjClock, IReadOnlyList<AutoDjClockTrack>, Task<bool>>? takeAir = null,
        Action<AutoDjClock, IReadOnlyList<AutoDjClockTrack>>? previewReady = null)
    {
        _supportedExtensions = (supportedExtensions ?? Array.Empty<string>())
            .Concat(new[] { ".rmw" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _takeAir = takeAir;
        _previewReady = previewReady;
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (!CanGenerate) return;
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        IsGenerating = true;
        OnPropertyChanged(nameof(CanGenerate));
        PreviewTracks.Clear();
        HasPreview = false;
        PreviewSummaryText = string.Empty;
        GenerationProgress = 0;
        IsProgressIndeterminate = true;
        GenerationProgressText = "Buscando archivos de audio...";
        StatusMessage = "Preparando biblioteca...";

        var clock = BuildClock();
        var progress = new Progress<PlaylistGeneratorProgress>(p =>
        {
            StatusMessage = p.Message;
            IsProgressIndeterminate = p.Total <= 0;
            if (p.Total > 0)
            {
                GenerationProgress = Math.Clamp(100.0 * p.Current / p.Total, 0, 100);
                GenerationProgressText = $"{p.Message} ({p.Current:N0}/{p.Total:N0})";
            }
            else
            {
                GenerationProgressText = p.Message;
            }
        });
        try
        {
            var result = await Task.Run(
                () => AutoDjClockService.Generate(clock, _supportedExtensions, progress, token), token);
            if (!result.Success)
            {
                _previewTracks = Array.Empty<AutoDjClockTrack>();
                IsProgressIndeterminate = false;
                StatusMessage = result.ErrorMessage ?? "No se pudo generar el reloj.";
                GenerationProgressText = StatusMessage;
                return;
            }

            _previewTracks = result.Tracks;
            _previewReady?.Invoke(clock, _previewTracks);

            int number = 1;
            foreach (var item in result.Tracks)
            {
                var tags = item.Candidate.Tags;
                PreviewTracks.Add(new AutoDjClockPreviewItem
                {
                    Number = number++,
                    Title = !string.IsNullOrWhiteSpace(tags.DisplayTitle)
                        ? tags.DisplayTitle
                        : item.Candidate.DisplayName,
                    Artist = tags.Performers,
                    GenreSlot = string.IsNullOrWhiteSpace(item.SlotGenre) ? "Cualquier género" : item.SlotGenre,
                    FilePath = item.Candidate.FilePath,
                    DurationSeconds = item.Candidate.DurationSeconds
                });
            }

            HasPreview = true;
            GenerationProgress = 100;
            IsProgressIndeterminate = false;
            GenerationProgressText = $"Lista generada: {result.LibraryTrackCount:N0} archivos analizados.";
            var actual = TimeSpan.FromSeconds(result.ActualDurationSeconds);
            var target = TimeSpan.FromSeconds(result.TargetDurationSeconds);
            var diff = TimeSpan.FromSeconds(Math.Abs(result.DifferenceSeconds));
            string relation = Math.Abs(result.DifferenceSeconds) < 1
                ? "ajustada al objetivo"
                : result.DifferenceSeconds > 0 ? $"excede {diff:mm\\:ss}" : $"faltan {diff:mm\\:ss}";
            PreviewSummaryText = $"{result.Tracks.Count} pistas · {AudioFileInfoReader.FormatDuration(actual.TotalSeconds)} / {AudioFileInfoReader.FormatDuration(target.TotalSeconds)}";
            StatusMessage = $"Vista previa lista; duración {relation}. " +
                $"Biblioteca: {result.LibraryTrackCount} pista(s), {result.SkippedUnreadable} ilegible(s).";
            if (result.SkippedClockSlots > 0)
                StatusMessage += $" Espacios sin pista disponible: {result.SkippedClockSlots}.";
        }
        catch (OperationCanceledException)
        {
            IsProgressIndeterminate = false;
            StatusMessage = "Escaneo cancelado.";
            GenerationProgressText = StatusMessage;
        }
        catch (Exception ex)
        {
            IsProgressIndeterminate = false;
            StatusMessage = $"Error al generar el reloj: {ex.Message}";
            GenerationProgressText = StatusMessage;
        }
        finally
        {
            IsGenerating = false;
            GenerateCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanGenerate));
            OnPropertyChanged(nameof(CanSavePlaylist));
        }
    }

    [RelayCommand(CanExecute = nameof(CanTakeAir))]
    private async Task TakeAirAsync()
    {
        if (!CanTakeAir || _takeAir is null) return;
        IsTakingAir = true;
        TakeAirAccepted = false;
        OnPropertyChanged(nameof(CanTakeAir));
        try
        {
            StatusMessage = "Preparando AutoDJ y esperando un punto seguro para tomar el aire...";
            TakeAirAccepted = await _takeAir(BuildClock(), _previewTracks);
            if (TakeAirAccepted)
                StatusMessage = "AutoDJ quedó habilitado. Tomará el aire al terminar la pista actual.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo habilitar AutoDJ: {ex.Message}";
        }
        finally
        {
            IsTakingAir = false;
            OnPropertyChanged(nameof(CanTakeAir));
            TakeAirCommand.NotifyCanExecuteChanged();
        }
    }

    public AutoDjClock BuildClock() => new()
    {
        SourceFolder = SourceFolder.Trim(),
        IncludeSubfolders = IncludeSubfolders,
        TargetDurationMinutes = Math.Clamp(TargetDurationMinutes, 5, AutoDjClock.MaxTargetDurationMinutes),
        MinTracksBetweenSameArtist = Math.Clamp(MinTracksBetweenSameArtist, 0, 50),
        Slots = ParseSlots()
    };

    public void LoadClock(string path)
    {
        var clock = AutoDjClockStore.Load(path);
        if (clock is null)
        {
            StatusMessage = "No se pudo abrir el archivo de reloj.";
            return;
        }

        SourceFolder = clock.SourceFolder ?? string.Empty;
        IncludeSubfolders = clock.IncludeSubfolders;
        TargetDurationMinutes = Math.Clamp(clock.TargetDurationMinutes, 5, AutoDjClock.MaxTargetDurationMinutes);
        MinTracksBetweenSameArtist = Math.Clamp(clock.MinTracksBetweenSameArtist, 0, 50);
        ClockSequenceText = string.Join(Environment.NewLine,
            (clock.Slots ?? new()).Select(slot => string.IsNullOrWhiteSpace(slot.Genre) ? "*" : slot.Genre));
        CurrentClockPath = path;
        ClearPreview();
        StatusMessage = $"Reloj cargado: {Path.GetFileName(path)}.";
    }

    public void SaveClock(string path)
    {
        try
        {
            AutoDjClockStore.Save(BuildClock(), path);
            CurrentClockPath = path.EndsWith(AutoDjClockStore.FileExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : path + AutoDjClockStore.FileExtension;
            StatusMessage = $"Reloj guardado: {Path.GetFileName(CurrentClockPath)}.";
            OnPropertyChanged(nameof(HasCurrentClock));
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo guardar el reloj: {ex.Message}";
        }
    }

    public void SavePlaylist(string path)
    {
        try
        {
            var playlist = new Playlist
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Mode = PlaylistMode.Once,
                Tracks = PreviewTracks.Select(item => new PlaylistTrack
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    DurationSeconds = item.DurationSeconds
                }).ToList()
            };
            PlaylistStore.Save(playlist, path);
            StatusMessage = $"Playlist AutoDJ guardada: {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo guardar la playlist: {ex.Message}";
        }
    }

    public void CancelPendingOperation() => _scanCts?.Cancel();

    private List<AutoDjClockSlot> ParseSlots() => ClockSequenceText
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => new AutoDjClockSlot { Genre = line == "*" ? string.Empty : line })
        .ToList();

    private void ClearPreview()
    {
        _previewTracks = Array.Empty<AutoDjClockTrack>();
        PreviewTracks.Clear();
        HasPreview = false;
        PreviewSummaryText = string.Empty;
        OnPropertyChanged(nameof(CanSavePlaylist));
    }

    partial void OnSourceFolderChanged(string value) => InvalidatePreview();
    partial void OnIncludeSubfoldersChanged(bool value) => InvalidatePreview();
    partial void OnTargetDurationMinutesChanged(int value) => InvalidatePreview();
    partial void OnMinTracksBetweenSameArtistChanged(int value) => InvalidatePreview();
    partial void OnClockSequenceTextChanged(string value) => InvalidatePreview();
    partial void OnIsTakingAirChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTakeAir));
        TakeAirCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTakeAir));
        TakeAirCommand.NotifyCanExecuteChanged();
    }

    private void InvalidatePreview()
    {
        ClearPreview();
        GenerateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanGenerate));
    }
}
