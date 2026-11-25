using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del contenido de una pestaña Tanda.
///
/// Edita UNA sola tanda completa (nombre, notas, pistas) y permite
/// abrirla/guardarla como archivo .tnd.
///
/// Se diferencia de un item Tanda dentro de un ScheduledList en que este
/// es un editor dedicado (no esta asociado a un horario programado).
/// -------------
public sealed partial class TandaEditorViewModel : ViewModelBase
{
    private Tanda _tanda;
    private string? _currentFilePath;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private string _tandaName;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.TandaEditor.Status.Initial");

    public ObservableCollection<TandaTrackViewModel> Tracks { get; } = new();

    /// -------------Se dispara cuando cambia el titulo logico de la pestaña.</summary>
    public event EventHandler<string>? TitleChanged;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string TitleWithDirty => IsDirty ? TandaName + " *" : TandaName;

    public int TrackCount => Tracks.Count;
    public int EnabledCount => Tracks.Count(t => t.IsEnabled);
    public double TotalDelaySeconds => Tracks.Where(t => t.IsEnabled).Sum(t => t.DelayBeforeSeconds);

    public bool HasTracks => Tracks.Count > 0;
    public bool HasNoTracks => !HasTracks;

    public string SummaryText =>
        string.Format(LocalizationService.GetString("Loc.TandaEditor.Summary"), TrackCount, EnabledCount, TotalDelaySeconds);

    // ───── Constructor ───────────────────────────────────────────────────

    public TandaEditorViewModel()
        : this(new Tanda { Name = LocalizationService.GetString("Loc.TandaEditor.NewTanda") })
    {
    }

    public TandaEditorViewModel(Tanda tanda)
    {
        _tanda = tanda;
        _tandaName = tanda.Name;
        _notes = tanda.Notes;

        RebuildTracksFromModel();
    }

    private void RebuildTracksFromModel()
    {
        foreach (var vm in Tracks)
            vm.Changed -= OnTrackChanged;
        Tracks.Clear();

        foreach (var track in _tanda.Tracks)
        {
            var vm = new TandaTrackViewModel(track);
            vm.Changed += OnTrackChanged;
            Tracks.Add(vm);
        }

        RefreshSummary();
    }

    // ───── Comandos: pistas ──────────────────────────────────────────────

    [RelayCommand]
    private void AddTrack()
    {
        var track = new TandaTrack { IsEnabled = true };
        var vm = new TandaTrackViewModel(track);
        vm.Changed += OnTrackChanged;

        Tracks.Add(vm);
        _tanda.Tracks.Add(track);

        IsDirty = true;
        StatusMessage = LocalizationService.GetString("Loc.TandaEditor.Status.TrackAdded");
        RefreshSummary();
    }

    [RelayCommand]
    private void RemoveTrack(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        if (!Tracks.Contains(trackVm)) return;

        trackVm.Changed -= OnTrackChanged;
        Tracks.Remove(trackVm);
        _tanda.Tracks.Remove(trackVm.Source);

        IsDirty = true;
        StatusMessage = LocalizationService.GetString("Loc.TandaEditor.Status.TrackRemoved");
        RefreshSummary();
    }

    [RelayCommand]
    private void MoveTrackUp(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = Tracks.IndexOf(trackVm);
        if (idx <= 0) return;

        Tracks.Move(idx, idx - 1);
        SyncTrackOrderToModel();
        IsDirty = true;
    }

    [RelayCommand]
    private void MoveTrackDown(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = Tracks.IndexOf(trackVm);
        if (idx < 0 || idx >= Tracks.Count - 1) return;

        Tracks.Move(idx, idx + 1);
        SyncTrackOrderToModel();
        IsDirty = true;
    }

    /// -------------Agrega una pista a partir de un archivo (util para drag&drop futuro).</summary>
    public TandaTrackViewModel AddTrackFromFile(string filePath)
    {
        var track = new TandaTrack
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            IsEnabled = true
        };

        var vm = new TandaTrackViewModel(track);
        vm.Changed += OnTrackChanged;

        Tracks.Add(vm);
        _tanda.Tracks.Add(track);

        IsDirty = true;
        RefreshSummary();
        return vm;
    }

    // ───── Apertura / guardado .tnd ─────────────────────────────────────

    public bool LoadFromFile(string filePath)
    {
        var loaded = TandaStore.Load(filePath);
        if (loaded is null)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.TandaEditor.Status.LoadFailed"), Path.GetFileName(filePath));
            return false;
        }

        _tanda = loaded;
        TandaName = loaded.Name;
        Notes = loaded.Notes;
        RebuildTracksFromModel();

        _currentFilePath = filePath;
        LoadedFilePath = filePath;
        IsDirty = false;

        StatusMessage = string.Format(LocalizationService.GetString("Loc.TandaEditor.Status.Loaded"), Path.GetFileName(filePath), Tracks.Count);
        TitleChanged?.Invoke(this, TandaName);
        return true;
    }

    public bool SaveToFile(string? filePath = null)
    {
        var target = filePath ?? _currentFilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = LocalizationService.GetString("Loc.TandaEditor.Status.NoSavePath");
            return false;
        }

        try
        {
            SyncTracksToModel();
            _tanda.Name = TandaName ?? string.Empty;
            _tanda.Notes = Notes ?? string.Empty;

            TandaStore.Save(_tanda, target);

            if (!target.EndsWith(TandaStore.FileExtension, StringComparison.OrdinalIgnoreCase))
                target += TandaStore.FileExtension;

            _currentFilePath = target;
            LoadedFilePath = target;
            IsDirty = false;

            StatusMessage = string.Format(LocalizationService.GetString("Loc.TandaEditor.Status.Saved"), Path.GetFileName(target));
            OnPropertyChanged(nameof(TitleWithDirty));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.TandaEditor.Status.SaveError"), ex.Message);
            return false;
        }
    }

    public void NewTanda()
    {
        _tanda = new Tanda { Name = LocalizationService.GetString("Loc.TandaEditor.NewTanda") };
        TandaName = _tanda.Name;
        Notes = string.Empty;
        RebuildTracksFromModel();

        _currentFilePath = null;
        LoadedFilePath = null;
        IsDirty = false;

        StatusMessage = LocalizationService.GetString("Loc.TandaEditor.Status.NewTanda");
        TitleChanged?.Invoke(this, TandaName);
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    private void SyncTracksToModel()
    {
        _tanda.Tracks.Clear();
        foreach (var vm in Tracks)
        {
            vm.ApplyToModel();
            _tanda.Tracks.Add(vm.Source);
        }
    }

    private void SyncTrackOrderToModel() => SyncTracksToModel();

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnTandaNameChanged(string value)
    {
        _tanda.Name = value ?? string.Empty;
        OnPropertyChanged(nameof(TitleWithDirty));
        IsDirty = true;
        TitleChanged?.Invoke(this, value ?? string.Empty);
    }

    partial void OnNotesChanged(string value)
    {
        _tanda.Notes = value ?? string.Empty;
        IsDirty = true;
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TitleWithDirty));

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        IsDirty = true;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(TotalDelaySeconds));
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(HasNoTracks));
        OnPropertyChanged(nameof(SummaryText));
    }
}
