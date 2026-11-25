using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

public sealed partial class AutoDjClockPreviewItem : ObservableObject
{
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    [ObservableProperty] private string _slotGenre = string.Empty;
    public string GenreSlot => string.IsNullOrWhiteSpace(SlotGenre)
        ? LocalizationService.GetString("Loc.AutoDJ.AnyGenre")
        : SlotGenre;
    public string FilePath { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public string DurationText => AudioFileInfoReader.FormatDuration(DurationSeconds);

    public void RefreshLanguage() => OnPropertyChanged(nameof(GenreSlot));

    partial void OnSlotGenreChanged(string value) => OnPropertyChanged(nameof(GenreSlot));
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
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.Initial");
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
        LocalizationService.LanguageChanged += OnLanguageChanged;
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
        GenerationProgressText = LocalizationService.GetString("Loc.AutoDJ.Progress.Searching");
        StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.Preparing");

        var clock = BuildClock();
        var progress = new Progress<PlaylistGeneratorProgress>(p =>
        {
            StatusMessage = LocalizeProgress(p.Message);
            IsProgressIndeterminate = p.Total <= 0;
            if (p.Total > 0)
            {
                GenerationProgress = Math.Clamp(100.0 * p.Current / p.Total, 0, 100);
                GenerationProgressText = string.Format(LocalizationService.GetString("Loc.AutoDJ.Progress.WithCount"), LocalizeProgress(p.Message), p.Current, p.Total);
            }
            else
            {
                GenerationProgressText = LocalizeProgress(p.Message);
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
                StatusMessage = LocalizeError(result.ErrorMessage ?? "No se pudo generar el reloj.");
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
                    SlotGenre = item.SlotGenre,
                    FilePath = item.Candidate.FilePath,
                    DurationSeconds = item.Candidate.DurationSeconds
                });
            }

            HasPreview = true;
            GenerationProgress = 100;
            IsProgressIndeterminate = false;
            GenerationProgressText = string.Format(LocalizationService.GetString("Loc.AutoDJ.Progress.LibraryAnalyzed"), result.LibraryTrackCount);
            var actual = TimeSpan.FromSeconds(result.ActualDurationSeconds);
            var target = TimeSpan.FromSeconds(result.TargetDurationSeconds);
            var diff = TimeSpan.FromSeconds(Math.Abs(result.DifferenceSeconds));
            string relation = Math.Abs(result.DifferenceSeconds) < 1
                ? LocalizationService.GetString("Loc.AutoDJ.Relation.OnTarget")
                : result.DifferenceSeconds > 0
                    ? string.Format(LocalizationService.GetString("Loc.AutoDJ.Relation.Exceeds"), diff.ToString("mm\\:ss"))
                    : string.Format(LocalizationService.GetString("Loc.AutoDJ.Relation.ShortBy"), diff.ToString("mm\\:ss"));
            PreviewSummaryText = string.Format(LocalizationService.GetString("Loc.AutoDJ.PreviewSummary"), result.Tracks.Count, AudioFileInfoReader.FormatDuration(actual.TotalSeconds), AudioFileInfoReader.FormatDuration(target.TotalSeconds));
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.PreviewReady"), relation, result.LibraryTrackCount, result.SkippedUnreadable);
            if (result.SkippedClockSlots > 0)
                StatusMessage += " " + string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.SkippedSlots"), result.SkippedClockSlots);
        }
        catch (OperationCanceledException)
        {
            IsProgressIndeterminate = false;
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.Cancelled");
            GenerationProgressText = StatusMessage;
        }
        catch (Exception ex)
        {
            IsProgressIndeterminate = false;
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.GenerateError"), ex.Message);
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
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.TakingAir");
            TakeAirAccepted = await _takeAir(BuildClock(), _previewTracks);
            if (TakeAirAccepted)
                StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.TakeAirEnabled");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.TakeAirError"), ex.Message);
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
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.OpenError");
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
        StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.ClockLoaded"), Path.GetFileName(path));
    }

    public void SaveClock(string path)
    {
        try
        {
            AutoDjClockStore.Save(BuildClock(), path);
            CurrentClockPath = path.EndsWith(AutoDjClockStore.FileExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : path + AutoDjClockStore.FileExtension;
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.ClockSaved"), Path.GetFileName(CurrentClockPath));
            OnPropertyChanged(nameof(HasCurrentClock));
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.ClockSaveError"), ex.Message);
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
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.PlaylistSaved"), Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AutoDJ.Status.PlaylistSaveError"), ex.Message);
        }
    }

    public void CancelPendingOperation() => _scanCts?.Cancel();

    public void Dispose()
    {
        CancelPendingOperation();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
    }

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

    private static string LocalizeProgress(string message)
    {
        if (message.StartsWith("Leyendo biblioteca ", StringComparison.Ordinal))
            return LocalizationService.GetString("Loc.AutoDJ.Progress.ReadingLibrary") + message["Leyendo biblioteca ".Length..];
        if (message.StartsWith("Organizando categorías ", StringComparison.Ordinal))
            return LocalizationService.GetString("Loc.AutoDJ.Progress.OrganizingCategories") + message["Organizando categorías ".Length..];
        return message switch
        {
            "Buscando archivos de audio..." => LocalizationService.GetString("Loc.AutoDJ.Progress.Searching"),
            "Biblioteca analizada. Organizando categorías del reloj..." => LocalizationService.GetString("Loc.AutoDJ.Progress.Categorizing"),
            "Generando la secuencia AutoDJ..." => LocalizationService.GetString("Loc.AutoDJ.Progress.Sequence"),
            _ => message
        };
    }

    private static string LocalizeError(string message) => message switch
    {
        "No se especificó una carpeta de biblioteca." => LocalizationService.GetString("Loc.AutoDJ.Error.NoLibraryFolder"),
        "La carpeta seleccionada no existe." => LocalizationService.GetString("Loc.AutoDJ.Error.MissingFolder"),
        "No se encontraron archivos de audio compatibles." => LocalizationService.GetString("Loc.AutoDJ.Error.NoCompatibleFiles"),
        "No se pudo leer la duración de ningún archivo de audio." => LocalizationService.GetString("Loc.AutoDJ.Error.NoReadableAudio"),
        "La duración objetivo debe estar entre 5 minutos y 24 horas." => LocalizationService.GetString("Loc.AutoDJ.Error.InvalidDuration"),
        "No hay un reloj configurado." => LocalizationService.GetString("Loc.AutoDJ.Error.NoClock"),
        "Agrega al menos un espacio al reloj." => LocalizationService.GetString("Loc.AutoDJ.Error.EmptyClock"),
        "No se pudo generar el reloj." => LocalizationService.GetString("Loc.AutoDJ.Error.Generate"),
        "No se pudo leer la biblioteca." => LocalizationService.GetString("Loc.AutoDJ.Error.ReadLibrary"),
        _ => message
    };

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsGenerating)
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.Preparing");
        else if (!HasPreview)
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.Status.Initial");
        else
        {
            foreach (var item in PreviewTracks) item.RefreshLanguage();
            StatusMessage = LocalizationService.GetString("Loc.AutoDJ.PreviewAvailable");
            PreviewSummaryText = string.Format(LocalizationService.GetString("Loc.AutoDJ.PreviewSummary"), PreviewTracks.Count,
                AudioFileInfoReader.FormatDuration(_previewTracks.Sum(x => x.Candidate.DurationSeconds)),
                AudioFileInfoReader.FormatDuration(TargetDurationMinutes * 60));
        }
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
