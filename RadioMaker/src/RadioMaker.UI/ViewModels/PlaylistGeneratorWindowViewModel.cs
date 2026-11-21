using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Item de preview del generador.
/// -------------
public sealed class PlaylistGeneratorPreviewItem
{
    public int Index { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public string Subtitle { get; init; } = string.Empty;

    public string DisplayName =>
        string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileNameWithoutExtension(FilePath);

    public string DurationText =>
        DurationSeconds > 0.5
            ? AudioFileInfoReader.FormatDuration(DurationSeconds)
            : "—";

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
}

/// -------------
/// ViewModel del modal "Generar playlist automaticamente".
/// -------------
public sealed partial class PlaylistGeneratorWindowViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;

    /// -------------Evita que los cambios automaticos limpien el preview cuando
    /// estamos aplicando un preset (todo cambia a la vez).</summary>
    private bool _suppressAutoClear;

    /// -------------Evita que la asignacion programatica de SelectedPreset
    /// dispare el reapply del preset en loop.</summary>
    private bool _suppressPresetApply;

    // ───── Flags del contexto ────────────────────────────────────────────

    public bool HasExistingContent { get; }
    public int ExistingTrackCount { get; }
    public string ExistingPlaylistName { get; }

    // ───── Preset actual ─────────────────────────────────────────────────

    [ObservableProperty] private string? _currentPresetPath;
    [ObservableProperty] private string _currentPresetName = string.Empty;

    /// -------------
    /// Lista de presets recientes para el combo. Se carga al construir el VM.
    /// -------------
    public ObservableCollection<PresetHistoryEntry> AvailablePresets { get; } = new();

    [ObservableProperty] private PresetHistoryEntry? _selectedPreset;

    public bool HasCurrentPreset => !string.IsNullOrEmpty(CurrentPresetPath);

    // ───── Opciones basicas ──────────────────────────────────────────────

    [ObservableProperty] private string _sourceFolder = string.Empty;
    [ObservableProperty] private bool _includeSubfolders;
    [ObservableProperty] private PlaylistGeneratorMode _mode = PlaylistGeneratorMode.TotalDuration;
    [ObservableProperty] private int _targetTrackCount = 20;
    [ObservableProperty] private double _targetDurationMinutes = 60.0;
    [ObservableProperty] private double _toleranceMinutes = 3.0;
    [ObservableProperty] private PlaylistGeneratorOrder _order = PlaylistGeneratorOrder.Random;

    public PlaylistGeneratorMode[] AvailableModes { get; } =
        { PlaylistGeneratorMode.TrackCount, PlaylistGeneratorMode.TotalDuration };

    public PlaylistGeneratorOrder[] AvailableOrders { get; } =
        { PlaylistGeneratorOrder.Random,
          PlaylistGeneratorOrder.AlphabeticalByName,
          PlaylistGeneratorOrder.AlphabeticalByPath };

    // ───── Filtros por tags ──────────────────────────────────────────────

    [ObservableProperty] private string _filterGenre = string.Empty;
    [ObservableProperty] private string _filterArtist = string.Empty;
    [ObservableProperty] private string _filterAlbum = string.Empty;

    [ObservableProperty] private bool _isYearRangeFilterEnabled;
    [ObservableProperty] private int _filterYearMin = 1900;
    [ObservableProperty] private int _filterYearMax = DateTime.Now.Year + 1;

    [ObservableProperty] private bool _isDurationRangeFilterEnabled;
    [ObservableProperty] private double _filterMinDurationMinutes = 0.5;
    [ObservableProperty] private double _filterMaxDurationMinutes = 10.0;

    // ───── Variedad ──────────────────────────────────────────────────────

    [ObservableProperty] private bool _avoidSameArtistConsecutive;
    [ObservableProperty] private bool _isMinTracksBetweenEnabled;
    [ObservableProperty] private int _minTracksBetweenSameArtist = 5;
    [ObservableProperty] private bool _balanceByArtist;
    [ObservableProperty] private bool _relaxIfNotEnough = true;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public bool HasActiveTagFilters =>
        !string.IsNullOrWhiteSpace(FilterGenre)
        || !string.IsNullOrWhiteSpace(FilterArtist)
        || !string.IsNullOrWhiteSpace(FilterAlbum)
        || IsYearRangeFilterEnabled
        || HasActiveVarietyRules;

    public bool HasActiveVarietyRules =>
        AvoidSameArtistConsecutive
        || IsMinTracksBetweenEnabled
        || BalanceByArtist;

    public int ActiveFilterCount
    {
        get
        {
            int n = 0;
            if (!string.IsNullOrWhiteSpace(FilterGenre)) n++;
            if (!string.IsNullOrWhiteSpace(FilterArtist)) n++;
            if (!string.IsNullOrWhiteSpace(FilterAlbum)) n++;
            if (IsYearRangeFilterEnabled) n++;
            if (IsDurationRangeFilterEnabled) n++;
            return n;
        }
    }

    public int ActiveVarietyRuleCount
    {
        get
        {
            int n = 0;
            if (AvoidSameArtistConsecutive) n++;
            if (IsMinTracksBetweenEnabled) n++;
            if (BalanceByArtist) n++;
            return n;
        }
    }

    public string FiltersSummaryText
    {
        get
        {
            int n = ActiveFilterCount;
            return n == 0 ? "Sin filtros activos" : $"{n} filtro(s) activo(s)";
        }
    }

    public string VarietySummaryText
    {
        get
        {
            int n = ActiveVarietyRuleCount;
            return n == 0 ? "Sin reglas de variedad" : $"{n} regla(s) activa(s)";
        }
    }

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string _statusMessage = "Configura las opciones y presiona Generar.";
    [ObservableProperty] private bool _hasPreview;

    // ───── Progreso ──────────────────────────────────────────────────────

    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private int _progressMaximum;
    [ObservableProperty] private bool _isProgressVisible;

    public bool IsProgressIndeterminate => ProgressMaximum <= 0 && IsProgressVisible;

    public ObservableCollection<PlaylistGeneratorPreviewItem> PreviewTracks { get; } = new();

    // ───── Propiedades derivadas del modo ───────────────────────────────

    public bool IsTrackCountMode => Mode == PlaylistGeneratorMode.TrackCount;
    public bool IsDurationMode => Mode == PlaylistGeneratorMode.TotalDuration;

    public string PreviewSummaryText
    {
        get
        {
            if (PreviewTracks.Count == 0) return string.Empty;

            double total = 0;
            foreach (var t in PreviewTracks) total += t.DurationSeconds;

            var ts = TimeSpan.FromSeconds(total);
            var dur = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{PreviewTracks.Count} pista(s) · {dur}";
        }
    }

    public bool CanGenerate =>
        !IsGenerating
        && !string.IsNullOrWhiteSpace(SourceFolder)
        && Directory.Exists(SourceFolder);

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaylistGeneratorWindowViewModel()
        : this(false, 0, string.Empty)
    {
    }

    public PlaylistGeneratorWindowViewModel(bool hasExistingContent, int existingTrackCount, string existingPlaylistName)
    {
        HasExistingContent = hasExistingContent;
        ExistingTrackCount = existingTrackCount;
        ExistingPlaylistName = existingPlaylistName ?? string.Empty;

        // Cargar historial de presets.
        ReloadHistory();

        // Auto-aplicar el preset mas reciente si existe.
        //
        // Nota: no asignamos SelectedPreset aca para no disparar dos veces
        // la carga (una por el setter de SelectedPreset, otra por la
        // llamada explicita a LoadPresetFromPath). Dejamos que
        // LoadPresetFromPath actualice la seleccion del combo al final.
        if (AvailablePresets.Count > 0)
        {
            LoadPresetFromPath(AvailablePresets[0].FilePath);
        }
    }

    // ───── API publica para el code-behind (file pickers) ───────────────

    /// -------------
    /// Aplica un preset cargado desde un archivo. Actualiza el historial
    /// y marca el preset como actual.
    /// -------------
    public void LoadPresetFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var preset = PlaylistGeneratorPresetStore.Load(path);
        if (preset is null)
        {
            StatusMessage = $"No se pudo cargar el preset '{Path.GetFileName(path)}'.";
            return;
        }

        // Aplicar las opciones al VM.
        ApplyOptions(preset.Options, preset.Name, path);

        // Actualizar historial.
        var updated = PlaylistGeneratorPresetHistoryStore.AddOrUpdate(
            AvailablePresets, path, preset.DisplayName);

        _suppressPresetApply = true;
        try
        {
            AvailablePresets.Clear();
            foreach (var e in updated) AvailablePresets.Add(e);

            SelectedPreset = AvailablePresets.FirstOrDefault(
                e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));
        }
        finally { _suppressPresetApply = false; }

        StatusMessage = $"Preset '{preset.DisplayName}' aplicado.";
    }

    /// -------------
    /// Guarda la configuracion actual como un nuevo preset.
    /// -------------
    public bool SavePresetAs(string path, string presetName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Ruta de guardado vacia.";
            return false;
        }

        try
        {
            var preset = BuildPreset(presetName);
            PlaylistGeneratorPresetStore.Save(preset, path);

            if (!path.EndsWith(PlaylistGeneratorPresetStore.FileExtension, StringComparison.OrdinalIgnoreCase))
                path += PlaylistGeneratorPresetStore.FileExtension;

            // Actualizar historial.
            var updated = PlaylistGeneratorPresetHistoryStore.AddOrUpdate(
                AvailablePresets, path, preset.DisplayName);

            _suppressPresetApply = true;
            try
            {
                AvailablePresets.Clear();
                foreach (var e in updated) AvailablePresets.Add(e);

                SelectedPreset = AvailablePresets.FirstOrDefault(
                    e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));
            }
            finally { _suppressPresetApply = false; }

            CurrentPresetPath = path;
            CurrentPresetName = preset.DisplayName;

            StatusMessage = $"Preset guardado en '{Path.GetFileName(path)}'.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar preset: {ex.Message}";
            return false;
        }
    }

    /// -------------
    /// Sobrescribe el preset actual con la configuracion vigente.
    /// -------------
    public bool SaveCurrentPreset()
    {
        if (string.IsNullOrEmpty(CurrentPresetPath))
        {
            StatusMessage = "No hay un preset cargado. Usa 'Guardar como...'.";
            return false;
        }

        return SavePresetAs(CurrentPresetPath, CurrentPresetName);
    }

    /// -------------Resetea todo el formulario a valores por defecto.</summary>
    [RelayCommand]
    private void NewPreset()
    {
        _suppressPresetApply = true;
        try
        {
            SelectedPreset = null;
            CurrentPresetPath = null;
            CurrentPresetName = string.Empty;

            // Reset de las opciones a sus valores por defecto.
            SourceFolder = string.Empty;
            IncludeSubfolders = false;
            Mode = PlaylistGeneratorMode.TotalDuration;
            TargetTrackCount = 20;
            TargetDurationMinutes = 60.0;
            ToleranceMinutes = 3.0;
            Order = PlaylistGeneratorOrder.Random;

            FilterGenre = string.Empty;
            FilterArtist = string.Empty;
            FilterAlbum = string.Empty;
            IsYearRangeFilterEnabled = false;
            FilterYearMin = 1900;
            FilterYearMax = DateTime.Now.Year + 1;
            IsDurationRangeFilterEnabled = false;
            FilterMinDurationMinutes = 0.5;
            FilterMaxDurationMinutes = 10.0;

            AvoidSameArtistConsecutive = false;
            IsMinTracksBetweenEnabled = false;
            MinTracksBetweenSameArtist = 5;
            BalanceByArtist = false;
            RelaxIfNotEnough = true;
        }
        finally { _suppressPresetApply = false; }

        ClearPreviewIfAny();
        StatusMessage = "Preset nuevo. Configura las opciones.";
    }

    /// -------------Quita el preset seleccionado del historial (no borra el archivo).</summary>
    [RelayCommand]
    private void RemoveFromHistory(PresetHistoryEntry? entry)
    {
        entry ??= SelectedPreset;
        if (entry is null) return;

        var updated = PlaylistGeneratorPresetHistoryStore.Remove(AvailablePresets, entry.FilePath);

        _suppressPresetApply = true;
        try
        {
            AvailablePresets.Clear();
            foreach (var e in updated) AvailablePresets.Add(e);

            if (string.Equals(SelectedPreset?.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
                SelectedPreset = null;
        }
        finally { _suppressPresetApply = false; }

        StatusMessage = $"Quitado del historial: {entry.DisplayName}";
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanGenerate) return;

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        IsGenerating = true;
        StatusMessage = "Iniciando...";

        ProgressValue = 0;
        ProgressMaximum = 0;

        PreviewTracks.Clear();
        HasPreview = false;

        var options = BuildOptions();
        var progress = new Progress<PlaylistGeneratorProgress>(p =>
        {
            StatusMessage = p.Message;
            ProgressMaximum = p.Total;
            ProgressValue = p.Current;
        });

        PlaylistGeneratorResult result;
        try
        {
            result = await Task.Run(
                () => PlaylistGeneratorService.Generate(options, progress, cts.Token),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generacion cancelada.";
            IsGenerating = false;
            return;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsGenerating = false;
            return;
        }

        IsGenerating = false;

        if (!result.Success)
        {
            StatusMessage = result.ErrorMessage ?? "No se pudo generar la playlist.";
            return;
        }

        if (result.TrackCount == 0)
        {
            StatusMessage = "No se pudo armar ninguna pista con esa configuracion.";
            return;
        }

        int i = 1;
        foreach (var c in result.SelectedTracks)
        {
            PreviewTracks.Add(new PlaylistGeneratorPreviewItem
            {
                Index = i++,
                FilePath = c.FilePath,
                DurationSeconds = c.DurationSeconds,
                Subtitle = c.SubtitleText
            });
        }

        HasPreview = true;

        var parts = new List<string>();
        parts.Add($"{result.TrackCount} pista(s) seleccionada(s) de {result.TotalCandidatesFound} valida(s)");

        if (result.SkippedUnreadable > 0)
            parts.Add($"{result.SkippedUnreadable} ilegible(s)");

        if (result.FilteredOutByTags > 0)
            parts.Add($"{result.FilteredOutByTags} descartada(s) por tags");

        if (result.FilteredOutByDuration > 0)
            parts.Add($"{result.FilteredOutByDuration} descartada(s) por duracion");

        if (result.RelaxedByArtistRules > 0)
            parts.Add($"{result.RelaxedByArtistRules} relajada(s) por variedad");

        StatusMessage = "Listo: " + string.Join(" · ", parts) + ".";
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void ReloadHistory()
    {
        AvailablePresets.Clear();
        var loaded = PlaylistGeneratorPresetHistoryStore.Load();
        foreach (var e in loaded)
            AvailablePresets.Add(e);
    }

    /// -------------
    /// Aplica las opciones de un preset al VM. Marca _suppressAutoClear
    /// durante toda la aplicacion para no disparar el ClearPreview en
    /// cada cambio de propiedad.
    /// -------------
    private void ApplyOptions(PlaylistGeneratorOptions options, string presetName, string presetPath)
    {
        if (options is null) return;

        _suppressAutoClear = true;
        try
        {
            SourceFolder = options.SourceFolder ?? string.Empty;
            IncludeSubfolders = options.IncludeSubfolders;
            Mode = options.Mode;
            TargetTrackCount = options.TargetTrackCount;
            TargetDurationMinutes = options.TargetDurationMinutes;
            ToleranceMinutes = options.ToleranceMinutes;
            Order = options.Order;

            FilterGenre = options.FilterGenre ?? string.Empty;
            FilterArtist = options.FilterArtist ?? string.Empty;
            FilterAlbum = options.FilterAlbum ?? string.Empty;
            IsYearRangeFilterEnabled = options.IsYearRangeFilterEnabled;
            FilterYearMin = options.FilterYearMin;
            FilterYearMax = options.FilterYearMax;
            IsDurationRangeFilterEnabled = options.IsDurationRangeFilterEnabled;
            FilterMinDurationMinutes = options.FilterMinDurationMinutes;
            FilterMaxDurationMinutes = options.FilterMaxDurationMinutes;

            AvoidSameArtistConsecutive = options.AvoidSameArtistConsecutive;
            IsMinTracksBetweenEnabled = options.MinTracksBetweenSameArtist > 0;
            MinTracksBetweenSameArtist = options.MinTracksBetweenSameArtist > 0
                ? options.MinTracksBetweenSameArtist
                : 5;
            BalanceByArtist = options.BalanceByArtist;
            RelaxIfNotEnough = options.RelaxIfNotEnough;

            CurrentPresetPath = presetPath;
            CurrentPresetName = presetName ?? string.Empty;
        }
        finally { _suppressAutoClear = false; }

        // Al aplicar un preset, el preview anterior ya no vale.
        PreviewTracks.Clear();
        HasPreview = false;

        // Notificar resumenes derivados.
        NotifyFilterSummaryChanged();
        NotifyVarietySummaryChanged();
        OnPropertyChanged(nameof(IsTrackCountMode));
        OnPropertyChanged(nameof(IsDurationMode));
        OnPropertyChanged(nameof(HasCurrentPreset));
    }

    /// -------------
    /// Construye un preset con la configuracion vigente del VM.
    /// -------------
    private PlaylistGeneratorPreset BuildPreset(string presetName)
    {
        return new PlaylistGeneratorPreset
        {
            Name = string.IsNullOrWhiteSpace(presetName) ? "Sin nombre" : presetName.Trim(),
            Options = BuildOptions()
        };
    }

    private PlaylistGeneratorOptions BuildOptions()
    {
        return new PlaylistGeneratorOptions
        {
            SourceFolder = SourceFolder?.Trim() ?? string.Empty,
            IncludeSubfolders = IncludeSubfolders,
            Mode = Mode,
            TargetTrackCount = Math.Max(1, TargetTrackCount),
            TargetDurationMinutes = Math.Max(1.0, TargetDurationMinutes),
            ToleranceMinutes = Math.Max(0.0, ToleranceMinutes),
            Order = Order,

            FilterGenre = (FilterGenre ?? string.Empty).Trim(),
            FilterArtist = (FilterArtist ?? string.Empty).Trim(),
            FilterAlbum = (FilterAlbum ?? string.Empty).Trim(),
            IsYearRangeFilterEnabled = IsYearRangeFilterEnabled,
            FilterYearMin = FilterYearMin,
            FilterYearMax = FilterYearMax,
            IsDurationRangeFilterEnabled = IsDurationRangeFilterEnabled,
            FilterMinDurationMinutes = Math.Max(0, FilterMinDurationMinutes),
            FilterMaxDurationMinutes = Math.Max(0, FilterMaxDurationMinutes),

            AvoidSameArtistConsecutive = AvoidSameArtistConsecutive,
            MinTracksBetweenSameArtist = IsMinTracksBetweenEnabled
                ? Math.Max(1, MinTracksBetweenSameArtist)
                : 0,
            BalanceByArtist = BalanceByArtist,
            RelaxIfNotEnough = RelaxIfNotEnough
        };
    }

    private void ClearPreviewIfAny()
    {
        if (_suppressAutoClear) return;
        if (!HasPreview && PreviewTracks.Count == 0) return;

        PreviewTracks.Clear();
        HasPreview = false;
        StatusMessage = "Cambiaste la configuracion. Presiona Regenerar.";
    }

    private void NotifyFilterSummaryChanged()
    {
        OnPropertyChanged(nameof(HasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FiltersSummaryText));
    }

    private void NotifyVarietySummaryChanged()
    {
        OnPropertyChanged(nameof(HasActiveVarietyRules));
        OnPropertyChanged(nameof(ActiveVarietyRuleCount));
        OnPropertyChanged(nameof(VarietySummaryText));
        OnPropertyChanged(nameof(HasActiveTagFilters));
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnSelectedPresetChanged(PresetHistoryEntry? value)
    {
        if (_suppressPresetApply) return;
        if (value is null) return;

        LoadPresetFromPath(value.FilePath);
    }

    partial void OnCurrentPresetPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCurrentPreset));
    }

    partial void OnModeChanged(PlaylistGeneratorMode value)
    {
        OnPropertyChanged(nameof(IsTrackCountMode));
        OnPropertyChanged(nameof(IsDurationMode));
        ClearPreviewIfAny();
    }

    partial void OnSourceFolderChanged(string value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        ClearPreviewIfAny();
    }

    partial void OnIncludeSubfoldersChanged(bool value) => ClearPreviewIfAny();
    partial void OnTargetTrackCountChanged(int value) => ClearPreviewIfAny();
    partial void OnTargetDurationMinutesChanged(double value) => ClearPreviewIfAny();
    partial void OnToleranceMinutesChanged(double value) => ClearPreviewIfAny();
    partial void OnOrderChanged(PlaylistGeneratorOrder value) => ClearPreviewIfAny();

    partial void OnFilterGenreChanged(string value) { ClearPreviewIfAny(); NotifyFilterSummaryChanged(); }
    partial void OnFilterArtistChanged(string value) { ClearPreviewIfAny(); NotifyFilterSummaryChanged(); }
    partial void OnFilterAlbumChanged(string value) { ClearPreviewIfAny(); NotifyFilterSummaryChanged(); }
    partial void OnIsYearRangeFilterEnabledChanged(bool value) { ClearPreviewIfAny(); NotifyFilterSummaryChanged(); }
    partial void OnFilterYearMinChanged(int value) { ClearPreviewIfAny(); }
    partial void OnFilterYearMaxChanged(int value) { ClearPreviewIfAny(); }
    partial void OnIsDurationRangeFilterEnabledChanged(bool value) { ClearPreviewIfAny(); NotifyFilterSummaryChanged(); }
    partial void OnFilterMinDurationMinutesChanged(double value) { ClearPreviewIfAny(); }
    partial void OnFilterMaxDurationMinutesChanged(double value) { ClearPreviewIfAny(); }

    partial void OnAvoidSameArtistConsecutiveChanged(bool value) { ClearPreviewIfAny(); NotifyVarietySummaryChanged(); }
    partial void OnIsMinTracksBetweenEnabledChanged(bool value) { ClearPreviewIfAny(); NotifyVarietySummaryChanged(); }
    partial void OnMinTracksBetweenSameArtistChanged(int value) { ClearPreviewIfAny(); }
    partial void OnBalanceByArtistChanged(bool value) { ClearPreviewIfAny(); NotifyVarietySummaryChanged(); }
    partial void OnRelaxIfNotEnoughChanged(bool value) { ClearPreviewIfAny(); }

    partial void OnHasPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewSummaryText));
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        IsProgressVisible = value;

        if (!value)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ProgressValue = 0;
                ProgressMaximum = 0;
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnProgressMaximumChanged(int value)
    {
        OnPropertyChanged(nameof(IsProgressIndeterminate));
    }

    public void CancelPendingOperation()
    {
        try { _cts?.Cancel(); } catch { }
    }
}