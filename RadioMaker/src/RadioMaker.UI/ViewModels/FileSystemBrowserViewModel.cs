using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Opcion del combo de filtro por extension del explorador.
///
/// Si Extension es null/vacio, significa "Todas las extensiones".
/// -------------
public sealed class FileExtensionFilter
{
    public string DisplayName { get; }
    public string? Extension { get; }

    public bool IsAll => string.IsNullOrEmpty(Extension);

    public FileExtensionFilter(string displayName, string? extension)
    {
        DisplayName = displayName ?? string.Empty;
        Extension = string.IsNullOrEmpty(extension)
            ? null
            : extension.ToLowerInvariant();
    }

    public override string ToString() => DisplayName;
}

/// -------------
/// ViewModel principal del File Manager (Modulo 3C).
///
/// Muestra archivos de audio Y proyectos nativos de RadioMaker
/// (.rmf / .tnd / .rms / .rcb). Los proyectos se detectan por extension
/// y se marcan visualmente con un badge de tipo.
///
/// Filtro por extension:
///   - "Todas las extensiones" (por defecto): muestra todo lo soportado.
///   - Una extension especifica: solo muestra archivos con esa extension.
/// -------------
public sealed partial class FileSystemBrowserViewModel : ViewModelBase
{
    private readonly IAudioEngine _engine;
    private CancellationTokenSource? _loadCts;
    private bool _suppressNodeNavigation;
    private bool _suppressFilterNavigation;

    // ───── Arbol de directorios ──────────────────────────────────────────

    public ObservableCollection<FileSystemNodeViewModel> RootNodes { get; } = new();

    [ObservableProperty] private FileSystemNodeViewModel? _selectedNode;

    // ───── Directorio actual ─────────────────────────────────────────────

    [ObservableProperty] private string _currentPath = string.Empty;

    public ObservableCollection<AudioFileViewModel> Files { get; } = new();

    [ObservableProperty] private AudioFileViewModel? _selectedFile;

    // ───── Filtros ───────────────────────────────────────────────────────

    /// -------------Opciones del combo de filtro por extension.</summary>
    public ObservableCollection<FileExtensionFilter> AvailableFilters { get; } = new();

    /// -------------Filtro seleccionado. Por defecto: "Todas las extensiones".</summary>
    [ObservableProperty] private FileExtensionFilter? _selectedFilter;

    /// -------------Filtro de busqueda por nombre (texto libre).</summary>
    [ObservableProperty] private string _searchFilter = string.Empty;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Navega a una carpeta para ver sus archivos.";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private int _totalCount;

    public SimpleBusSelectorViewModel BusSelector { get; }

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _nowPlayingText = string.Empty;

    private IAudioChannel? _currentChannel;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public bool HasFiles => Files.Count > 0;
    public bool HasNoFiles => !HasFiles;
    public bool HasSelection => SelectedFile is not null;
    public bool HasNoSelection => !HasSelection;

    public string FileCountText => TotalCount == 0
        ? "Sin archivos"
        : (FileCount == TotalCount
            ? $"{TotalCount} archivo(s)"
            : $"{FileCount} de {TotalCount} archivo(s)");

    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) &&
                           Path.GetDirectoryName(CurrentPath) is not null;

    // ───── Eventos ───────────────────────────────────────────────────────

    public FavoriteFolderRegistry? FavoriteFolders { get; }

    public event EventHandler<AudioFileViewModel>? FileActivated;

    // ───── Constructor ───────────────────────────────────────────────────

    public FileSystemBrowserViewModel(IAudioEngine engine, FavoriteFolderRegistry? favoriteFolders = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        FavoriteFolders = favoriteFolders;

        // Construir la lista de filtros ANTES de cualquier navegacion.
        // SuppressFilterNavigation evita que la asignacion inicial de
        // SelectedFilter dispare un NavigateToAsync prematuro.
        _suppressFilterNavigation = true;
        try
        {
            BuildAvailableFilters();
        }
        finally
        {
            _suppressFilterNavigation = false;
        }

        var savedExplorerBusId = RightPanelBusStore.GetExplorerBusId();
        IAudioBus? initialExplorerBus = null;

        if (!string.IsNullOrEmpty(savedExplorerBusId))
        {
            try
            {
                initialExplorerBus = engine.Buses.FirstOrDefault(b => b.Id == savedExplorerBusId);
            }
            catch { }
        }

        BusSelector = new SimpleBusSelectorViewModel(
            engine,
            initialBus: initialExplorerBus,
            onBusChanged: bus => RightPanelBusStore.SetExplorerBusId(bus.Id));

        try
        {
            var roots = FileSystemNodeViewModel.CreateRootNodes();
            foreach (var r in roots)
                RootNodes.Add(r);
        }
        catch { }

        if (FavoriteFolders is not null)
            FavoriteFolders.Changed += OnFavoritesChanged;

        var persisted = FileSystemBrowserStateStore.Load();
        var start = persisted.LastFolder;

        if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
                start = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
            _ = NavigateToAsync(start);
    }

    // ───── Construccion del filtro por extension ────────────────────────

    /// -------------
    /// Arma la lista de filtros del combo:
    ///   1. "Todas las extensiones" (default).
    ///   2. Proyectos nativos: .rmf, .tnd, .rms, .rcb (con descripcion).
    ///   3. Extensiones de audio soportadas por el engine (alfabetico).
    /// -------------
    private void BuildAvailableFilters()
    {
        AvailableFilters.Clear();

        // Opcion "Todas".
        AvailableFilters.Add(new FileExtensionFilter("Todas las extensiones", null));

        // Proyectos nativos primero (mas relevantes para radio).
        AvailableFilters.Add(new FileExtensionFilter(".rmf  — Playlist", ".rmf"));
        AvailableFilters.Add(new FileExtensionFilter(".rme  — Encadenadores", ".rme"));
        AvailableFilters.Add(new FileExtensionFilter(".tnd  — Tanda", ".tnd"));
        AvailableFilters.Add(new FileExtensionFilter(".rms  — Programados", ".rms"));
        AvailableFilters.Add(new FileExtensionFilter(".rcb  — Botonera", ".rcb"));

        // Extensiones de audio, en orden alfabetico.
        var audioExts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var e in _engine.SupportedExtensions)
            {
                var lower = e.ToLowerInvariant();

                // Evitar duplicados de proyectos.
                if (lower == ".rmf" || lower == ".rme" || lower == ".tnd" || lower == ".rms" || lower == ".rcb")
                    continue;

                audioExts.Add(lower);
            }
        }
        catch
        {
            foreach (var e in new[] { ".mp3", ".ogg", ".wav", ".aif", ".aiff", ".flac", ".m4a", ".aac", ".opus", ".wv" })
                audioExts.Add(e);
        }

        foreach (var e in audioExts)
            AvailableFilters.Add(new FileExtensionFilter(e, e));

        // Seleccion por defecto: "Todas".
        SelectedFilter = AvailableFilters[0];
    }

    // ───── Helper: extensiones soportadas (filtro-aware) ────────────────

    /// -------------
    /// Devuelve el set de extensiones a enumerar en el directorio actual.
    ///
    /// Si hay un filtro especifico seleccionado (distinto de "Todas"),
    /// devuelve solo esa extension. Si no, devuelve todas las soportadas:
    /// audios del engine + proyectos nativos.
    /// -------------
    private HashSet<string> GetSupportedExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Filtro especifico ────────────────────────────────────────────
        if (SelectedFilter is not null && !SelectedFilter.IsAll && !string.IsNullOrEmpty(SelectedFilter.Extension))
        {
            set.Add(SelectedFilter.Extension);
            return set;
        }

        // ── Todas las extensiones ────────────────────────────────────────
        try
        {
            foreach (var e in _engine.SupportedExtensions)
                set.Add(e.ToLowerInvariant());
        }
        catch
        {
            foreach (var e in new[] { ".mp3", ".ogg", ".wav", ".aif", ".aiff", ".flac", ".m4a", ".aac", ".opus", ".wv" })
                set.Add(e);
        }

        // Proyectos nativos de RadioMaker.
        set.Add(PlaylistStore.FileExtension);         // .rmf
        set.Add(EncadenadoresStore.FileExtension);   // .rme
        set.Add(TandaStore.FileExtension);            // .tnd
        set.Add(ScheduledListStore.FileExtension);    // .rms
        set.Add(CartBoardStore.FileExtension);        // .rcb

        return set;
    }

    // ───── Navegacion ────────────────────────────────────────────────────

    public async Task NavigateToAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        if (!Directory.Exists(folderPath)) return;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        CurrentPath = folderPath;
        OnPropertyChanged(nameof(CanGoUp));

        try
        {
            FileSystemBrowserStateStore.Save(new FileSystemBrowserState
            {
                LastFolder = folderPath
            });
        }
        catch { }

        _ = ExpandAndSelectNodeAsync(folderPath);

        IsLoading = true;
        StatusMessage = $"Leyendo '{Path.GetFileName(folderPath)}'...";

        Files.Clear();
        FileCount = 0;
        TotalCount = 0;
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasNoFiles));

        try
        {
            var supported = GetSupportedExtensions();

            var files = await Task.Run(() => EnumerateFiles(folderPath, supported), cts.Token);
            if (cts.IsCancellationRequested) return;

            TotalCount = files.Count;

            var filtered = ApplyFilter(files);
            FileCount = filtered.Count;

            foreach (var file in filtered)
            {
                if (cts.IsCancellationRequested) return;
                Files.Add(file);
            }

            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(FileCountText));

            // Mensaje adaptado al filtro activo.
            if (FileCount == 0)
            {
                StatusMessage = (SelectedFilter is not null && !SelectedFilter.IsAll)
                    ? $"Sin archivos {SelectedFilter.Extension} en esta carpeta."
                    : "Sin archivos compatibles en esta carpeta.";
            }
            else
            {
                StatusMessage = $"Cargando info de {FileCount} archivo(s)...";
            }

            _ = LoadTagsAsync(Files.ToList(), cts.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error leyendo carpeta: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static List<AudioFileViewModel> EnumerateFiles(string folder, HashSet<string> supported)
    {
        var result = new List<AudioFileViewModel>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                if (!supported.Contains(ext)) continue;

                try
                {
                    result.Add(new AudioFileViewModel(path));
                }
                catch { }
            }
        }
        catch { }

        return result.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<AudioFileViewModel> ApplyFilter(List<AudioFileViewModel> files)
    {
        if (string.IsNullOrWhiteSpace(SearchFilter)) return files;

        var needle = SearchFilter.Trim();
        return files
            .Where(f => f.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ───── Carga de TAGs con throttle ────────────────────────────────────

    private async Task LoadTagsAsync(List<AudioFileViewModel> files, CancellationToken token)
    {
        int loaded = 0;

        foreach (var file in files)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                await file.LoadTagsAsync();
                loaded++;

                if (loaded % 5 == 0)
                    StatusMessage = $"Cargando info... {loaded}/{files.Count}";
            }
            catch { }
        }

        if (!token.IsCancellationRequested)
            StatusMessage = $"Listo. {files.Count} archivo(s) leidos.";
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
            await NavigateToAsync(CurrentPath);
    }

    [RelayCommand]
    private async Task GoUp()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        var parent = Path.GetDirectoryName(CurrentPath);
        if (string.IsNullOrEmpty(parent)) return;

        await NavigateToAsync(parent);
    }

    [RelayCommand]
    private async Task GoHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
            await NavigateToAsync(home);
    }

    [RelayCommand]
    private async Task GoDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
            await NavigateToAsync(desktop);
    }

    /// -------------
    /// Activacion de un archivo (doble clic / Enter).
    ///   - Audio: reproduce en el bus propio del explorador.
    ///   - Proyecto nativo: no hace nada (hay que arrastrarlo).
    /// -------------
    [RelayCommand]
    private void ActivateSelectedFile()
    {
        if (SelectedFile is null) return;

        if (!SelectedFile.IsAudio)
        {
            StatusMessage = $"'{SelectedFile.FileName}' es un archivo de proyecto. Arrastralo a la pestaña que corresponda.";
            return;
        }

        FileActivated?.Invoke(this, SelectedFile);
        PlayFile(SelectedFile.FullPath);
    }

    // ───── Reproduccion ──────────────────────────────────────────────────

    public void PlayFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (!File.Exists(filePath))
        {
            AppLogger.Warning("FILE MANAGER PLAY", $"No existe el archivo solicitado: {filePath}");
            return;
        }

        if (BusSelector.SelectedBus is not IAudioBus bus)
        {
            StatusMessage = "No hay bus de salida seleccionado.";
            AppLogger.Warning("FILE MANAGER PLAY", "No se pudo reproducir: no hay bus de salida seleccionado.");
            return;
        }

        try
        {
            if (_currentChannel is not null)
                AppLogger.Info("FILE MANAGER PLAY", $"Vista previa reemplazada por: {Path.GetFileName(filePath)}.");
            StopCurrentChannel();

            var channel = bus.LoadFile(filePath);
            channel.Ended += OnChannelEnded;
            _currentChannel = channel;

            channel.Play();

            AppLogger.Info("FILE MANAGER PLAY",
                $"Bus={bus.DisplayName} | Reproduciendo: {Path.GetFileName(filePath)}.");

            IsPlaying = true;
            NowPlayingText = Path.GetFileName(filePath);
            StatusMessage = $"Reproduciendo: {NowPlayingText}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al reproducir: {ex.Message}";
            AppLogger.Error("FILE MANAGER PLAY", $"Error al reproducir '{filePath}'", ex);
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        if (_currentChannel is not null)
            AppLogger.Info("FILE MANAGER PLAY", $"Detención solicitada: {NowPlayingText}.");
        StopCurrentChannel();
        IsPlaying = false;
        NowPlayingText = string.Empty;
        StatusMessage = "Detenido.";
    }

    private void StopCurrentChannel()
    {
        var ch = _currentChannel;
        _currentChannel = null;
        if (ch is null) return;

        try
        {
            ch.Ended -= OnChannelEnded;
            ch.Stop();
            ch.Dispose();
        }
        catch { }
    }

    private void OnChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AppLogger.Info("FILE MANAGER PLAY",
                e.WasStoppedByUser
                    ? $"Vista previa detenida: {NowPlayingText}."
                    : $"Vista previa finalizada: {NowPlayingText}.");
            IsPlaying = false;
            NowPlayingText = string.Empty;
            StopCurrentChannel();
        });
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnSelectedNodeChanged(FileSystemNodeViewModel? value)
    {
        if (value is null) return;
        if (_suppressNodeNavigation) return;

        value.IsExpanded = true;
        _ = NavigateToAsync(value.FullPath);
    }

    partial void OnSelectedFileChanged(AudioFileViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));

        if (value is not null && !value.TagsLoaded)
            _ = value.LoadTagsAsync();
    }

    partial void OnSearchFilterChanged(string value)
    {
        _ = ReapplyFilterAsync();
    }

    partial void OnSelectedFilterChanged(FileExtensionFilter? value)
    {
        if (_suppressFilterNavigation) return;
        _ = ReapplyFilterAsync();
    }

    private async Task ReapplyFilterAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        await Task.CompletedTask;
        await NavigateToAsync(CurrentPath);
    }

    // ───── Expansion automatica del arbol ────────────────────────────────

    private async Task ExpandAndSelectNodeAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        string fullTarget;
        try { fullTarget = Path.GetFullPath(folderPath); }
        catch { return; }

        FileSystemNodeViewModel? current = null;
        foreach (var root in RootNodes)
        {
            string rootFull;
            try { rootFull = Path.GetFullPath(root.FullPath); }
            catch { continue; }

            if (fullTarget.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
                fullTarget.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                current = root;
                break;
            }
        }

        if (current is null) return;

        string rootPathFull;
        try { rootPathFull = Path.GetFullPath(current.FullPath); }
        catch { return; }

        if (fullTarget.Equals(rootPathFull, StringComparison.OrdinalIgnoreCase))
        {
            MarkNodeSelectedAndExpanded(current);
            return;
        }

        var remaining = fullTarget.Substring(rootPathFull.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(remaining))
        {
            MarkNodeSelectedAndExpanded(current);
            return;
        }

        var segments = remaining.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            current.IsExpanded = true;
            await current.EnsureChildrenLoadedAsync();

            FileSystemNodeViewModel? next = null;
            foreach (var child in current.Children)
            {
                var childName = Path.GetFileName(
                    child.FullPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));

                if (string.Equals(childName, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null) return;
            current = next;
        }

        MarkNodeSelectedAndExpanded(current);
    }

    private void MarkNodeSelectedAndExpanded(FileSystemNodeViewModel node)
    {
        _suppressNodeNavigation = true;
        try
        {
            node.IsExpanded = true;
            node.IsSelected = true;
            SelectedNode = node;
        }
        finally
        {
            _suppressNodeNavigation = false;
        }
    }

    // ───── Favoritos ─────────────────────────────────────────────────────

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        if (FavoriteFolders is null) return;

        foreach (var root in RootNodes)
            UpdateFavoriteFlagRecursive(root, FavoriteFolders);
    }

    private static void UpdateFavoriteFlagRecursive(
        FileSystemNodeViewModel node,
        FavoriteFolderRegistry registry)
    {
        try
        {
            node.IsFavorite = registry.IsFavorite(node.FullPath);
        }
        catch { }

        foreach (var child in node.Children)
            UpdateFavoriteFlagRecursive(child, registry);
    }

    public void Dispose()
    {
        StopCurrentChannel();
    }
}
