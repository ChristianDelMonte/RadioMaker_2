using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;
using RadioMaker.Core.Tags;

namespace RadioMaker.UI.ViewModels;

public sealed partial class EncadenadorTrackViewModel : ObservableObject
{
    public EncadenadorTrack Model { get; }
    public string DisplayTitle => Model.DisplayTitle;
    public string FilePath => Model.FilePath;
    public string DurationText => Model.DurationSeconds > 0
        ? TimeSpan.FromSeconds(Model.DurationSeconds).ToString(@"m\:ss")
        : "--:--";
    public EncadenadorTrackViewModel(EncadenadorTrack model) => Model = model;

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(DurationText));
    }
}

public sealed partial class EncadenadoresEditorViewModel : ViewModelBase
{
    private readonly EncadenadoresRegistry _registry;
    private readonly string _ownerId = Guid.NewGuid().ToString("N");
    private EncadenadoresSet _set;
    private bool _updatingActiveFromRegistry;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private string _statusMessage = "Agrega audios para crear una lista de encadenadores.";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private EncadenadorTrackViewModel? _selectedTrack;
    [ObservableProperty] private int _insertEvery = 1;
    [ObservableProperty] private string _selectedOrder = "En orden de lista";
    [ObservableProperty] private string _selectedCounterReset = "Al cambiar de fuente";
    [ObservableProperty] private bool _isActive;

    public ObservableCollection<EncadenadorTrackViewModel> Tracks { get; } = new();
    public string TitleWithDirty => IsDirty ? Name + " *" : Name;
    public bool HasTracks => Tracks.Count > 0;
    public bool HasSelection => SelectedTrack is not null;
    public int[] InsertIntervals { get; } = new[] { 1, 2, 3 };
    public string[] OrderOptions { get; } = new[] { "En orden de lista", "Aleatorio" };
    public string[] CounterResetOptions { get; } = new[] { "Continuo", "Al cambiar de fuente" };

    public EncadenadoresEditorViewModel(EncadenadoresRegistry registry, EncadenadoresSet? set = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _set = set ?? new EncadenadoresSet();
        _name = string.IsNullOrWhiteSpace(_set.Name) ? "Encadenadores" : _set.Name;
        _insertEvery = Math.Clamp(_set.InsertEvery, 1, 3);
        _selectedOrder = _set.Order == EncadenadorOrder.Random ? OrderOptions[1] : OrderOptions[0];
        _selectedCounterReset = _set.CounterReset == EncadenadorCounterReset.Continuous
            ? CounterResetOptions[0]
            : CounterResetOptions[1];
        RebuildTracks();
        _registry.ActiveSetChanged += OnRegistryActiveSetChanged;
        IsActive = _registry.IsActive(_ownerId);
        if (_set.IsActive && !IsActive) SetActive(true);
    }

    partial void OnNameChanged(string value)
    {
        _set.Name = value;
        MarkDirty();
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (_updatingActiveFromRegistry) return;
        if (value) _registry.Activate(_ownerId, _set);
        else _registry.Deactivate(_ownerId);
        _set.IsActive = value;
        MarkDirty();
    }

    partial void OnSelectedTrackChanged(EncadenadorTrackViewModel? value) =>
        OnPropertyChanged(nameof(HasSelection));

    partial void OnInsertEveryChanged(int value)
    {
        _set.InsertEvery = Math.Clamp(value, 1, 3);
        MarkDirty();
        if (IsActive) _registry.Update(_ownerId, _set);
    }

    partial void OnSelectedOrderChanged(string value)
    {
        _set.Order = value == OrderOptions[1] ? EncadenadorOrder.Random : EncadenadorOrder.Sequential;
        MarkDirty();
        if (IsActive) _registry.Update(_ownerId, _set);
    }

    partial void OnSelectedCounterResetChanged(string value)
    {
        _set.CounterReset = value == CounterResetOptions[0]
            ? EncadenadorCounterReset.Continuous
            : EncadenadorCounterReset.OnSourceChange;
        MarkDirty();
        if (IsActive) _registry.Update(_ownerId, _set);
    }

    public void SetActive(bool active)
    {
        IsActive = active;
    }

    public void AddFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
        if (Tracks.Any(t => string.Equals(Path.GetFullPath(t.FilePath), Path.GetFullPath(filePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            return;

        double duration = 0;
        try { duration = AudioFileInfoReader.GetDurationSeconds(filePath); } catch { }
        var model = new EncadenadorTrack
        {
            FilePath = Path.GetFullPath(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            DurationSeconds = Math.Max(0, duration)
        };
        var vm = new EncadenadorTrackViewModel(model);
        Tracks.Add(vm);
        _set.Tracks.Add(model);
        RefreshDerived();
        MarkDirty();
    }

    public void UpdateTrack(EncadenadorTrackViewModel track, string filePath, string title)
    {
        if (!Tracks.Contains(track) || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var fullPath = Path.GetFullPath(filePath);
        var pathChanged = !string.Equals(track.Model.FilePath, fullPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        track.Model.FilePath = fullPath;
        track.Model.Title = title.Trim();
        if (pathChanged)
        {
            try { track.Model.DurationSeconds = Math.Max(0, AudioFileInfoReader.GetDurationSeconds(fullPath)); }
            catch { track.Model.DurationSeconds = 0; }
        }
        track.Refresh();
        MarkDirty();
        if (IsActive) _registry.Update(_ownerId, _set);
    }

    [RelayCommand]
    private void RemoveSelected(EncadenadorTrackViewModel? track)
    {
        var target = track ?? SelectedTrack;
        if (target is null || !Tracks.Contains(target)) return;
        var index = Tracks.IndexOf(target);
        _set.Tracks.Remove(target.Model);
        Tracks.Remove(target);
        SelectedTrack = Tracks.Count == 0 ? null : Tracks[Math.Clamp(index, 0, Tracks.Count - 1)];
        RefreshDerived();
        MarkDirty();
    }

    [RelayCommand]
    private void MoveTrackUp(EncadenadorTrackViewModel? track) => MoveTrack(track, -1);

    [RelayCommand]
    private void MoveTrackDown(EncadenadorTrackViewModel? track) => MoveTrack(track, 1);

    private void MoveTrack(EncadenadorTrackViewModel? track, int delta)
    {
        if (track is null) return;
        var index = Tracks.IndexOf(track);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Tracks.Count) return;
        Tracks.Move(index, target);
        _set.Tracks.RemoveAt(index);
        _set.Tracks.Insert(target, track.Model);
        MarkDirty();
    }

    [RelayCommand]
    private void NewSet()
    {
        if (IsActive) SetActive(false);
        _set = new EncadenadoresSet();
        Name = "Encadenadores";
        LoadedFilePath = null;
        InsertEvery = 1;
        SelectedOrder = OrderOptions[0];
        SelectedCounterReset = CounterResetOptions[1];
        RebuildTracks();
        IsDirty = false;
        StatusMessage = "Nueva lista de encadenadores.";
    }

    public bool LoadFromFile(string path)
    {
        var loaded = EncadenadoresStore.Load(path);
        if (loaded is null)
        {
            StatusMessage = "No se pudo abrir el archivo .rme.";
            return false;
        }

        if (IsActive) SetActive(false);
        _set = loaded;
        Name = string.IsNullOrWhiteSpace(loaded.Name) ? Path.GetFileNameWithoutExtension(path) : loaded.Name;
        InsertEvery = Math.Clamp(loaded.InsertEvery, 1, 3);
        SelectedOrder = loaded.Order == EncadenadorOrder.Random ? OrderOptions[1] : OrderOptions[0];
        SelectedCounterReset = loaded.CounterReset == EncadenadorCounterReset.Continuous
            ? CounterResetOptions[0]
            : CounterResetOptions[1];
        LoadedFilePath = path;
        RebuildTracks();
        IsDirty = false;
        StatusMessage = $"Abierto: {Path.GetFileName(path)}";
        if (_set.IsActive) SetActive(true);
        return true;
    }

    public bool SaveToFile(string? path = null)
    {
        var target = path ?? LoadedFilePath;
        if (string.IsNullOrWhiteSpace(target)) return false;
        try
        {
            _set.Name = Name;
            _set.Tracks = Tracks.Select(t => t.Model).ToList();
            _set.InsertEvery = InsertEvery;
            _set.Order = SelectedOrder == OrderOptions[1] ? EncadenadorOrder.Random : EncadenadorOrder.Sequential;
            _set.CounterReset = SelectedCounterReset == CounterResetOptions[0]
                ? EncadenadorCounterReset.Continuous
                : EncadenadorCounterReset.OnSourceChange;
            _set.IsActive = IsActive;
            EncadenadoresStore.Save(_set, target);
            LoadedFilePath = target.EndsWith(EncadenadoresStore.FileExtension, StringComparison.OrdinalIgnoreCase)
                ? target
                : target + EncadenadoresStore.FileExtension;
            IsDirty = false;
            StatusMessage = $"Guardado: {Path.GetFileName(LoadedFilePath)}";
            if (IsActive) _registry.Update(_ownerId, _set);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error guardando: {ex.Message}";
            return false;
        }
    }

    private void RebuildTracks()
    {
        Tracks.Clear();
        _set.Tracks ??= new List<EncadenadorTrack>();
        foreach (var track in _set.Tracks)
        {
            if (track is null || string.IsNullOrWhiteSpace(track.FilePath)) continue;
            var vm = new EncadenadorTrackViewModel(track);
            Tracks.Add(vm);
        }
        SelectedTrack = Tracks.FirstOrDefault();
        RefreshDerived();
    }

    private void OnRegistryActiveSetChanged(object? sender, EventArgs e)
    {
        _updatingActiveFromRegistry = true;
        IsActive = _registry.IsActive(_ownerId);
        _updatingActiveFromRegistry = false;
        _set.IsActive = IsActive;
    }

    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(TitleWithDirty));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(TitleWithDirty));
    }

    public void Dispose()
    {
        _registry.ActiveSetChanged -= OnRegistryActiveSetChanged;
        if (IsActive) _registry.Deactivate(_ownerId);
    }
}
