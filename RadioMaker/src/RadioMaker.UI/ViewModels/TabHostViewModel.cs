using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Services;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del contenedor de pestañas.
///
/// Ahora es multi-zona: cada instancia sabe en que zona vive (Left, Center,
/// Right) y ajusta su comportamiento:
///   - Crea la pestaña fija correspondiente (Eventos / Cart / ninguna).
///   - Filtra los tipos de pestaña que ofrece el boton "+".
///   - Persiste la zona de cada pestaña en el workspace.
///
/// FASE B: las pestañas de Playlist ahora tienen su propio PlaylistPlayer
/// y su propio bus de salida (SimpleBusSelector). Se crean a traves de la
/// factory <see cref="PlaylistEditorFactory"/> que provee el MainWindowViewModel.
/// -------------
public sealed partial class TabHostViewModel : ViewModelBase
{
    // ───── Dependencias ──────────────────────────────────────────────────
    private readonly ScheduledListRegistry? _registry;
    private readonly PlaylistPlayer? _playlistPlayer;
    private readonly ModuleBusSelectorViewModel? _busSelectorScheduler;
    private readonly ModuleBusSelectorViewModel? _busSelectorPlaylist;
    private readonly FavoriteFolderRegistry? _favoriteRegistry;
    private readonly IAudioEngine? _audioEngine;
    private readonly RadioMaker.Core.Scheduling.PlaybackEngine? _playbackEngine;
    private readonly EncadenadoresRegistry? _encadenadoresRegistry;

    // ── FASE B: factory y cleanup de players de playlist ─────────────────
    /// -------------
    /// Factory que crea un nuevo PlaylistEditorViewModel con su propio
    /// PlaylistPlayer y SimpleBusSelector. La provee MainWindowViewModel.
    /// -------------
    public Func<PlaylistEditorViewModel>? PlaylistEditorFactory { get; init; }

    /// -------------
    /// Callback para liberar los recursos de una pestaña de playlist
    /// cerrada (detener el player, desuscribir, etc).
    /// -------------
    public Action<PlaylistEditorViewModel>? PlaylistEditorDisposer { get; init; }

    /// -------------Indica si la ScheduledList pre-existente ya fue consumida.</summary>
    private bool _scheduledListConsumed;

    // ───── Zona ──────────────────────────────────────────────────────────

    public TabZone Zone { get; }

    // ───── Colecciones y estado ──────────────────────────────────────────

    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    [ObservableProperty] private TabItemViewModel? _selectedTab;

    public ScheduledListViewModel? ScheduledList { get; }

    public TabItemViewModel? EventsTab { get; }
    public EventsPanelViewModel? EventsPanel { get; }

    public TabItemViewModel? CartTab { get; }

    public Func<CartBoardViewModel>? CartBoardFactory { get; init; }
    public Action<CartBoardViewModel>? CartBoardDisposer { get; init; }

    public event EventHandler? TabsChanged;
    public event EventHandler<TabItemViewModel?>? SelectionChanged;

    // ───── Flags de UI ───────────────────────────────────────────────────

    public bool IsLeftZone => Zone == TabZone.Left;
    public bool IsCenterZone => Zone == TabZone.Center;
    public bool IsRightZone => Zone == TabZone.Right;

    public bool CanAddCartTab => Zone == TabZone.Center;
    public bool ShowStandardTabs => Zone != TabZone.Right;

    // ═════════════════════════════════════════════════════════════════════
    // Constructores
    // ═════════════════════════════════════════════════════════════════════

    public TabHostViewModel()
        : this(TabZone.Left, null, null, null, null)
    {
    }

    public ObservableCollection<FavoriteFolderEntry> FavoriteFolders { get; } = new();

    public TabHostViewModel(
        TabZone zone,
        ScheduledListRegistry? registry,
        PlaylistPlayer? playlistPlayer = null,
        ModuleBusSelectorViewModel? busSelectorScheduler = null,
        ModuleBusSelectorViewModel? busSelectorPlaylist = null,
        object? fixedPanel = null,
        FavoriteFolderRegistry? favoriteRegistry = null,
        IAudioEngine? audioEngine = null,
        SchedulerPanelViewModel? schedulerPanel = null,
        RadioMaker.Core.Scheduling.PlaybackEngine? playbackEngine = null,
        EncadenadoresRegistry? encadenadoresRegistry = null)
    {
        Zone = zone;
        _registry = registry;
        _playlistPlayer = playlistPlayer;
        _busSelectorScheduler = busSelectorScheduler;
        _busSelectorPlaylist = busSelectorPlaylist;
        _favoriteRegistry = favoriteRegistry;
        _audioEngine = audioEngine;
        _playbackEngine = playbackEngine;
        _encadenadoresRegistry = encadenadoresRegistry;

        if (_favoriteRegistry is not null)
        {
            ReloadFavoriteFolders();
            _favoriteRegistry.Changed += OnFavoriteRegistryChanged;
        }

        // Pestaña fija segun la zona.
        switch (zone)
        {
            case TabZone.Left:
                EventsPanel = new EventsPanelViewModel(schedulerPanel);
                EventsTab = new TabItemViewModel(
                    title: LocalizationService.GetString("Loc.Tabs.Events"),
                    kind: TabKind.Events,
                    isClosable: false,
                    content: EventsPanel);
                Tabs.Add(EventsTab);
                break;

            case TabZone.Center:
                if (fixedPanel is CartBoardViewModel cartVm)
                {
                    CartTab = new TabItemViewModel(
                        title: cartVm.TitleWithDirty,
                        kind: TabKind.Cart,
                        isClosable: false,
                        content: cartVm);

                    cartVm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(CartBoardViewModel.BoardName) ||
                            e.PropertyName == nameof(CartBoardViewModel.IsDirty))
                        {
                            if (CartTab is not null)
                                CartTab.Title = cartVm.TitleWithDirty;
                        }
                    };

                    Tabs.Add(CartTab);
                }
                break;

            case TabZone.Right:
                if (fixedPanel is FileSystemBrowserViewModel fileVm)
                {
                    var fileTab = new TabItemViewModel(
                        title: LocalizationService.GetString("Loc.Tabs.FileManager"),
                        kind: TabKind.Custom,
                        isClosable: false,
                        content: fileVm,
                        localizedTitleKey: "Loc.Tabs.FileManager");

                    Tabs.Add(fileTab);
                }
                break;
        }

        // Pestaña Programados: solo en zonas izquierda y central.
        if (zone != TabZone.Right)
        {
            ScheduledList = new ScheduledListViewModel(_playbackEngine);
            ScheduledList.BusSelector = _busSelectorScheduler;

            var scheduledTab = CreateScheduledTab(ScheduledList);
            Tabs.Add(scheduledTab);
        }

        // Seleccion inicial: pestaña fija de la zona.
        if (Tabs.Count > 0)
            SelectedTab = Tabs[0];
    }

    // ═════════════════════════════════════════════════════════════════════
    // CREACION DE PESTAÑAS
    // ═════════════════════════════════════════════════════════════════════

    private TabItemViewModel CreateScheduledTab(ScheduledListViewModel vm)
    {
        if (vm.BusSelector is null)
            vm.BusSelector = _busSelectorScheduler;

        RegisterScheduledList(vm);

        var tab = new TabItemViewModel(
            title: vm.TitleWithDirty,
            kind: TabKind.Scheduled,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScheduledListViewModel.ListName) ||
                e.PropertyName == nameof(ScheduledListViewModel.IsDirty))
            {
                tab.Title = vm.TitleWithDirty;
            }
        };

        tab.TitleCommitted += (_, newTitle) =>
        {
            try
            {
                if (vm.ListName != newTitle)
                    vm.ListName = newTitle;
            }
            catch { }
        };

        return tab;
    }

    private TabItemViewModel CreateTandaTab(TandaEditorViewModel vm)
    {
        var tab = new TabItemViewModel(
            title: vm.TitleWithDirty,
            kind: TabKind.Tanda,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TandaEditorViewModel.TandaName) ||
                e.PropertyName == nameof(TandaEditorViewModel.IsDirty))
            {
                tab.Title = vm.TitleWithDirty;
            }
        };

        tab.TitleCommitted += (_, newTitle) =>
        {
            try
            {
                if (vm.TandaName != newTitle)
                    vm.TandaName = newTitle;
            }
            catch { }
        };

        return tab;
    }

    private TabItemViewModel CreatePlaylistTab(PlaylistEditorViewModel vm)
    {
        // ── FASE B: el BusSelector ya lo crea la factory ────────────────
        // No lo pisamos con el compartido. Si la factory no lo seteo,
        // no hacemos nada (queda sin selector, que es aceptable en tests).

        var tab = new TabItemViewModel(
            title: vm.TitleWithDirty,
            kind: TabKind.Playlist,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaylistEditorViewModel.PlaylistName) ||
                e.PropertyName == nameof(PlaylistEditorViewModel.IsDirty))
            {
                tab.Title = vm.TitleWithDirty;
            }
        };

        tab.TitleCommitted += (_, newTitle) =>
        {
            try
            {
                if (vm.PlaylistName != newTitle)
                    vm.PlaylistName = newTitle;
            }
            catch { }
        };

        return tab;
    }

    private TabItemViewModel CreateEncadenadoresTab(EncadenadoresEditorViewModel vm)
    {
        var tab = new TabItemViewModel(
            title: vm.TitleWithDirty,
            kind: TabKind.Encadenadores,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EncadenadoresEditorViewModel.Name) ||
                e.PropertyName == nameof(EncadenadoresEditorViewModel.IsDirty))
                tab.Title = vm.TitleWithDirty;
            if (e.PropertyName == nameof(EncadenadoresEditorViewModel.IsActive))
                TabsChanged?.Invoke(this, EventArgs.Empty);
        };

        tab.TitleCommitted += (_, newTitle) => vm.Name = newTitle;
        return tab;
    }

    private TabItemViewModel CreateNotesTab(NotesEditorViewModel vm)
    {
        var tab = new TabItemViewModel(
            title: vm.DisplayName,
            kind: TabKind.Notes,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.TitleChanged += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                tab.Title = vm.DisplayName;
            });
        };

        return tab;
    }

    private TabItemViewModel CreateCartTab(CartBoardViewModel vm)
    {
        // ── FASE C: el BusSelector ya lo crea la factory de MainWindowViewModel
        // (SimpleBusSelectorViewModel propio por pestaña).

        var tab = new TabItemViewModel(
            title: vm.TitleWithDirty,
            kind: TabKind.Cart,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CartBoardViewModel.BoardName) ||
                e.PropertyName == nameof(CartBoardViewModel.IsDirty))
            {
                tab.Title = vm.TitleWithDirty;
            }
        };

        tab.TitleCommitted += (_, newTitle) =>
        {
            try
            {
                if (vm.BoardName != newTitle)
                    vm.BoardName = newTitle;
            }
            catch { }
        };

        return tab;
    }

    private TabItemViewModel CreateFavoriteFolderTab(FavoriteFolderPanelViewModel vm)
    {
        var tab = new TabItemViewModel(
            title: vm.DisplayName,
            kind: TabKind.FavoriteFolder,
            isClosable: true,
            content: vm,
            onCloseRequested: t => CloseTab(t));

        return tab;
    }

    // ═════════════════════════════════════════════════════════════════════
    // COMANDOS
    // ═════════════════════════════════════════════════════════════════════

    [RelayCommand]
    public void AddScheduledTab()
    {
        var vm = new ScheduledListViewModel(_playbackEngine);
        vm.ListName = string.Format(LocalizationService.GetString("Loc.Tabs.Default.Scheduled"), CountOf(TabKind.Scheduled) + 1);

        var tab = CreateScheduledTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    [RelayCommand]
    public void AddTandaTab()
    {
        var vm = new TandaEditorViewModel();
        vm.TandaName = string.Format(LocalizationService.GetString("Loc.Tabs.Default.Sequence"), CountOf(TabKind.Tanda) + 1);

        var tab = CreateTandaTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    [RelayCommand]
    public void AddPlaylistTab()
    {
        // ── FASE B: usar la factory si esta disponible ──────────────────
        PlaylistEditorViewModel vm;

        if (PlaylistEditorFactory is not null)
        {
            vm = PlaylistEditorFactory();
        }
        else
        {
            // Fallback para tests o casos sin factory: crear con el player
            // compartido y el BusSelector del modulo.
            vm = new PlaylistEditorViewModel(
                new Playlist { Name = string.Format(LocalizationService.GetString("Loc.Tabs.Default.Playlist"), CountOf(TabKind.Playlist) + 1) },
                _playlistPlayer);

            if (_busSelectorPlaylist is not null)
            {
                // En este caso, no hay selector simple: usamos el modulo.
                // Pero como el tipo cambio, esta rama solo se usa en tests.
            }
        }

        if (string.IsNullOrWhiteSpace(vm.PlaylistName))
            vm.PlaylistName = string.Format(LocalizationService.GetString("Loc.Tabs.Default.Playlist"), CountOf(TabKind.Playlist) + 1);

        var tab = CreatePlaylistTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    /// <summary>Creates or refreshes the dedicated AutoDJ playlist tab.</summary>
    public void ShowAutoDjPlaylist(PlaylistEditorViewModel vm)
    {
        if (Zone != TabZone.Left || vm is null) return;

        var existing = Tabs.FirstOrDefault(t => t.Content is PlaylistEditorViewModel pl && pl.IsAutoDjPlaylist);
        if (existing is not null)
        {
            SelectedTab = existing;
            TabsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var tab = new TabItemViewModel(
            title: "AUTODJ",
            kind: TabKind.Playlist,
            isClosable: false,
            content: vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void AddEncadenadoresTab()
    {
        if (Zone == TabZone.Right || _encadenadoresRegistry is null) return;
        var vm = new EncadenadoresEditorViewModel(_encadenadoresRegistry);
        var tab = CreateEncadenadoresTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    [RelayCommand]
    public void AddNotesTab()
    {
        var draftId = Guid.NewGuid().ToString("N");
        var vm = new NotesEditorViewModel(draftId);

        var tab = CreateNotesTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    [RelayCommand]
    public void AddCartTab()
    {
        if (Zone != TabZone.Center) return;
        if (CartBoardFactory is null) return;

        CartBoardViewModel vm;
        try
        {
            vm = CartBoardFactory();
        }
        catch
        {
            return;
        }

        if (vm is null) return;

        var tab = CreateCartTab(vm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
    }

    [RelayCommand]
    public void AddFavoriteFolderTab(FavoriteFolderEntry? entry)
    {
        if (Zone != TabZone.Right) return;
        if (entry is null) return;
        if (_audioEngine is null) return;

        var existing = Tabs.FirstOrDefault(t =>
            t.Kind == TabKind.FavoriteFolder &&
            t.Content is FavoriteFolderPanelViewModel fVm &&
            fVm.FavoriteId == entry.Id);

        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        if (!entry.ExistsNow)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.Tabs.FavoriteFolderMissing"), entry.Path);
            return;
        }

        var panelVm = new FavoriteFolderPanelViewModel(entry, _audioEngine);
        var tab = CreateFavoriteFolderTab(panelVm);
        Tabs.Add(tab);
        SelectedTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty] private string _statusMessage = string.Empty;

    private void ReloadFavoriteFolders()
    {
        FavoriteFolders.Clear();
        if (_favoriteRegistry is null) return;

        foreach (var f in _favoriteRegistry.GetAll())
            FavoriteFolders.Add(f);
    }

    private void OnFavoriteRegistryChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(ReloadFavoriteFolders);
    }

    // ═════════════════════════════════════════════════════════════════════
    // CERRAR / RENOMBRAR
    // ═════════════════════════════════════════════════════════════════════

    public bool CloseTab(TabItemViewModel tab)
    {
        if (tab is null) return false;
        if (!tab.IsClosable) return false;
        if (!Tabs.Contains(tab)) return false;

        if (tab.Kind == TabKind.Scheduled && tab.Content is ScheduledListViewModel schVm)
            UnregisterScheduledList(schVm);

        if (tab.Content is NotesEditorViewModel notesVm)
        {
            try { notesVm.Dispose(); } catch { }
        }

        if (tab.Content is EncadenadoresEditorViewModel encadenadoresVm)
        {
            try { encadenadoresVm.Dispose(); } catch { }
        }

        // ── FASE B: liberar el player de playlist ───────────────────────
        if (tab.Kind == TabKind.Playlist && tab.Content is PlaylistEditorViewModel plVm)
        {
            try { PlaylistEditorDisposer?.Invoke(plVm); } catch { }
        }

        if (tab.Kind == TabKind.Cart && tab.Content is CartBoardViewModel cartVm)
        {
            try { CartBoardDisposer?.Invoke(cartVm); } catch { }
        }

        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (SelectedTab == tab)
        {
            if (Tabs.Count == 0)
                SelectedTab = null;
            else
                SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }

        TabsChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspace();
        return true;
    }

    public void RenameTab(TabItemViewModel tab, string newTitle)
    {
        if (tab is null) return;
        if (string.IsNullOrWhiteSpace(newTitle)) return;

        tab.Title = newTitle.Trim();
        tab.IsDirty = true;
        SaveWorkspace();
    }

    public TabItemViewModel? FindFirst(TabKind kind) =>
        Tabs.FirstOrDefault(t => t.Kind == kind);

    public int CountOf(TabKind kind) =>
        Tabs.Count(t => t.Kind == kind);

    public bool HasPlaylists => CountOf(TabKind.Playlist) > 0;

    partial void OnSelectedTabChanged(TabItemViewModel? value)
    {
        SetSelectedTabInternal(value);
        SelectionChanged?.Invoke(this, value);
    }

    private void SetSelectedTabInternal(TabItemViewModel? selected)
    {
        foreach (var tab in Tabs)
            tab.IsSelected = ReferenceEquals(tab, selected);
    }

    // ═════════════════════════════════════════════════════════════════════
    // REGISTRO EN EL SCHEDULER
    // ═════════════════════════════════════════════════════════════════════

    private void RegisterScheduledList(ScheduledListViewModel vm)
    {
        if (_registry is null) return;
        try
        {
            var list = vm.GetInternalList();
            if (list is not null)
            {
                _registry.Register(list, vm.ListName);
                // Sincronizar el estado enabled inicial.
                _registry.SetEnabled(list, vm.IsEnabled);
            }

            // Suscribir para cambios futuros del switch.
            vm.RegistryStateChanged -= OnScheduledListRegistryStateChanged;
            vm.RegistryStateChanged += OnScheduledListRegistryStateChanged;
        }
        catch { }
    }

    private void OnScheduledListRegistryStateChanged(object? sender, EventArgs e)
    {
        if (_registry is null) return;
        if (sender is not ScheduledListViewModel vm) return;

        try
        {
            var list = vm.GetInternalList();
            if (list is not null)
                _registry.SetEnabled(list, vm.IsEnabled);
        }
        catch { }
    }

    private void UnregisterScheduledList(ScheduledListViewModel vm)
    {
        if (_registry is null) return;
        try
        {
            vm.RegistryStateChanged -= OnScheduledListRegistryStateChanged;
            var list = vm.GetInternalList();
            if (list is not null)
                _registry.Unregister(list);
        }
        catch { }
    }

    public bool AddFileToTab(TabItemViewModel tab, string filePath)
    {
        if (tab is null) return false;
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (!File.Exists(filePath)) return false;

        try
        {
            switch (tab.Content)
            {
                case PlaylistEditorViewModel playlist:
                    playlist.AddTrackFromFile(filePath);
                    return true;

                case EncadenadoresEditorViewModel encadenadores:
                    encadenadores.AddFile(filePath);
                    return true;

                case ScheduledListViewModel scheduled:
                    scheduled.AddFileAsItem(filePath);
                    return true;

                case TandaEditorViewModel tanda:
                    tanda.AddTrackFromFile(filePath);
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // PERSISTENCIA DEL WORKSPACE
    // ═════════════════════════════════════════════════════════════════════

    private WorkspaceTabZone GetWorkspaceZone() => Zone switch
    {
        TabZone.Center => WorkspaceTabZone.Center,
        TabZone.Right => WorkspaceTabZone.Right,
        _ => WorkspaceTabZone.Left
    };

    public List<WorkspaceTabEntry> BuildWorkspaceEntries()
    {
        var entries = new List<WorkspaceTabEntry>();
        var zone = GetWorkspaceZone();

        foreach (var tab in Tabs)
        {
            if (tab.Kind == TabKind.Events) continue;
            if (!tab.IsClosable) continue;

            var entry = new WorkspaceTabEntry { Zone = zone };

            switch (tab.Kind)
            {
                case TabKind.Scheduled:
                    entry.Kind = WorkspaceTabKind.Scheduled;
                    if (tab.Content is ScheduledListViewModel slVm)
                    {
                        entry.FilePath = slVm.LoadedFilePath ?? string.Empty;
                        entry.Title = slVm.ListName ?? string.Empty;
                    }
                    break;

                case TabKind.Tanda:
                    entry.Kind = WorkspaceTabKind.Tanda;
                    if (tab.Content is TandaEditorViewModel tVm)
                    {
                        entry.FilePath = tVm.LoadedFilePath ?? string.Empty;
                        entry.Title = tVm.TandaName ?? string.Empty;
                    }
                    break;

                case TabKind.Playlist:
                    entry.Kind = WorkspaceTabKind.Playlist;
                    if (tab.Content is PlaylistEditorViewModel pVm)
                    {
                        entry.FilePath = pVm.LoadedFilePath ?? string.Empty;
                        entry.Title = pVm.PlaylistName ?? string.Empty;
                    }
                    break;

                case TabKind.Encadenadores:
                    entry.Kind = WorkspaceTabKind.Encadenadores;
                    if (tab.Content is EncadenadoresEditorViewModel eVm)
                    {
                        entry.FilePath = eVm.LoadedFilePath ?? string.Empty;
                        entry.Title = eVm.Name ?? string.Empty;
                        entry.IsActive = eVm.IsActive;
                    }
                    break;

                case TabKind.Notes:
                    entry.Kind = WorkspaceTabKind.Notes;
                    if (tab.Content is NotesEditorViewModel nVm)
                    {
                        entry.FilePath = nVm.LoadedFilePath ?? string.Empty;
                        entry.Title = nVm.DisplayName;
                        entry.DraftId = nVm.DraftId;
                    }
                    break;

                case TabKind.Cart:
                    entry.Kind = WorkspaceTabKind.Cart;
                    if (tab.Content is CartBoardViewModel cVm)
                    {
                        entry.FilePath = cVm.LoadedFilePath ?? string.Empty;
                        entry.Title = cVm.BoardName ?? string.Empty;
                    }
                    break;

                case TabKind.FavoriteFolder:
                    entry.Kind = WorkspaceTabKind.FavoriteFolder;
                    if (tab.Content is FavoriteFolderPanelViewModel fVm)
                    {
                        entry.FavoriteId = fVm.FavoriteId;
                        entry.Title = fVm.DisplayName;
                    }
                    break;

                default:
                    continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    public void RefreshLocalizedTitles()
    {
        foreach (var tab in Tabs)
            tab.RefreshLocalizedTitle();
    }

    public void SelectClosableTabByIndex(int index)
    {
        if (index < 0) return;

        int current = 0;
        foreach (var tab in Tabs)
        {
            if (!tab.IsClosable) continue;

            if (current == index)
            {
                SelectedTab = tab;
                return;
            }

            current++;
        }

        if (Tabs.Count > 0)
            SelectedTab = Tabs[^1];
    }

    public bool TryRestoreEntry(WorkspaceTabEntry entry, out TabItemViewModel? createdTab)
    {
        createdTab = null;
        if (entry is null) return false;
        if (entry.Zone != GetWorkspaceZone()) return false;

        try
        {
            switch (entry.Kind)
            {
                case WorkspaceTabKind.Scheduled:
                    {
                        if (!_scheduledListConsumed && ScheduledList is not null)
                        {
                            _scheduledListConsumed = true;

                            if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            {
                                try { ScheduledList.LoadFromFile(entry.FilePath); } catch { }
                            }

                            if (!string.IsNullOrEmpty(entry.Title) && entry.Title != "Programados")
                            {
                                ScheduledList.ListName = entry.Title;
                                ScheduledList.IsDirty = false;
                            }

                            return true;
                        }

                        var vm = new ScheduledListViewModel(_playbackEngine);
                        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            vm.LoadFromFile(entry.FilePath);

                        if (!string.IsNullOrEmpty(entry.Title) && string.IsNullOrEmpty(vm.LoadedFilePath))
                        {
                            vm.ListName = entry.Title;
                            vm.IsDirty = false;
                        }

                        var tab = CreateScheduledTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.Tanda:
                    {
                        var vm = new TandaEditorViewModel();
                        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            vm.LoadFromFile(entry.FilePath);

                        if (!string.IsNullOrEmpty(entry.Title) && string.IsNullOrEmpty(vm.LoadedFilePath))
                        {
                            vm.TandaName = entry.Title;
                            vm.IsDirty = false;
                        }

                        var tab = CreateTandaTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.Playlist:
                    {
                        // ── FASE B: usar la factory para crear el editor ───
                        PlaylistEditorViewModel vm;

                        if (PlaylistEditorFactory is not null)
                        {
                            vm = PlaylistEditorFactory();
                        }
                        else
                        {
                            vm = new PlaylistEditorViewModel(new Playlist(), _playlistPlayer);
                        }

                        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            vm.LoadFromFile(entry.FilePath);

                        if (!string.IsNullOrEmpty(entry.Title) && string.IsNullOrEmpty(vm.LoadedFilePath))
                        {
                            vm.PlaylistName = entry.Title;
                            vm.IsDirty = false;
                        }

                        var tab = CreatePlaylistTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.Encadenadores:
                    {
                        if (Zone == TabZone.Right || _encadenadoresRegistry is null) return false;
                        var vm = new EncadenadoresEditorViewModel(_encadenadoresRegistry);
                        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            vm.LoadFromFile(entry.FilePath);
                        if (!string.IsNullOrWhiteSpace(entry.Title) && string.IsNullOrEmpty(vm.LoadedFilePath))
                        {
                            vm.Name = entry.Title;
                            vm.IsDirty = false;
                        }
                        if (entry.IsActive) vm.SetActive(true);
                        var tab = CreateEncadenadoresTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.Notes:
                    {
                        var draftId = string.IsNullOrEmpty(entry.DraftId)
                            ? Guid.NewGuid().ToString("N")
                            : entry.DraftId;

                        var initialPath = (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            ? entry.FilePath
                            : null;

                        var vm = new NotesEditorViewModel(draftId, initialPath);
                        var tab = CreateNotesTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.Cart:
                    {
                        if (Zone != TabZone.Center) return false;
                        if (CartBoardFactory is null) return false;

                        var vm = CartBoardFactory();
                        if (vm is null) return false;

                        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                            vm.LoadFromFile(entry.FilePath);

                        var tab = CreateCartTab(vm);

                        if (!string.IsNullOrEmpty(entry.Title))
                        {
                            try
                            {
                                vm.BoardName = entry.Title;
                                vm.IsDirty = false;
                            }
                            catch { }
                        }

                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }

                case WorkspaceTabKind.FavoriteFolder:
                    {
                        if (Zone != TabZone.Right) return false;
                        if (_favoriteRegistry is null) return false;
                        if (_audioEngine is null) return false;
                        if (string.IsNullOrEmpty(entry.FavoriteId)) return false;

                        var favEntry = _favoriteRegistry.GetById(entry.FavoriteId);
                        if (favEntry is null) return false;

                        var vm = new FavoriteFolderPanelViewModel(favEntry, _audioEngine);
                        var tab = CreateFavoriteFolderTab(vm);
                        Tabs.Add(tab);
                        createdTab = tab;
                        return true;
                    }
            }
        }
        catch { }

        return false;
    }

    private void SaveWorkspace()
    {
        // La persistencia real la hace el MainWindowViewModel.
    }

    // ═════════════════════════════════════════════════════════════════════
    // APERTURA DE PROYECTOS NATIVOS POR DRAG & DROP
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Abre un proyecto nativo de RadioMaker (.rms / .tnd / .rmf / .rcb)
    /// en una pestaña NUEVA de esta zona.
    ///
    /// Se llama cuando el usuario suelta un archivo nativo sobre el TabHost.
    /// El tipo de pestaña se infiere por la extension:
    ///   - .rms -> Scheduled (Programados)
    ///   - .tnd -> Tanda
    ///   - .rmf -> Playlist
    ///   - .rcb -> Cart (solo en la zona Center)
    ///
    /// Reglas por zona:
    ///   - Left / Center: abren Programados, Tanda, Playlist.
    ///   - Center: ademas, abre Cart (.rcb).
    ///   - Right: NO abre pestañas nativas (solo favoritos).
    ///
    /// Devuelve true si se abrio la pestaña.
    /// -------------
    public bool OpenNativeProjectInNewTab(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path)) return false;

        var ext = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            switch (ext)
            {
                case ".rms":
                    {
                        if (Zone == TabZone.Right) return false;

                        var vm = new ScheduledListViewModel(_playbackEngine);
                        if (!vm.LoadFromFile(path)) return false;

                        var tab = CreateScheduledTab(vm);
                        Tabs.Add(tab);
                        SelectedTab = tab;
                        TabsChanged?.Invoke(this, EventArgs.Empty);
                        SaveWorkspace();
                        return true;
                    }

                case ".tnd":
                    {
                        if (Zone == TabZone.Right) return false;

                        var vm = new TandaEditorViewModel();
                        if (!vm.LoadFromFile(path)) return false;

                        var tab = CreateTandaTab(vm);
                        Tabs.Add(tab);
                        SelectedTab = tab;
                        TabsChanged?.Invoke(this, EventArgs.Empty);
                        SaveWorkspace();
                        return true;
                    }

                case ".rmf":
                    {
                        if (Zone == TabZone.Right) return false;

                        // ── FASE B: usar la factory para crear el editor
                        //    con su propio player y bus.
                        PlaylistEditorViewModel vm;

                        if (PlaylistEditorFactory is not null)
                        {
                            vm = PlaylistEditorFactory();
                        }
                        else
                        {
                            vm = new PlaylistEditorViewModel(new Playlist(), _playlistPlayer);
                        }

                        if (!vm.LoadFromFile(path)) return false;

                        var tab = CreatePlaylistTab(vm);
                        Tabs.Add(tab);
                        SelectedTab = tab;
                        TabsChanged?.Invoke(this, EventArgs.Empty);
                        SaveWorkspace();
                        return true;
                    }

                case ".rme":
                    {
                        if (Zone == TabZone.Right || _encadenadoresRegistry is null) return false;
                        var vm = new EncadenadoresEditorViewModel(_encadenadoresRegistry);
                        if (!vm.LoadFromFile(path))
                        {
                            vm.Dispose();
                            return false;
                        }
                        var tab = CreateEncadenadoresTab(vm);
                        Tabs.Add(tab);
                        SelectedTab = tab;
                        TabsChanged?.Invoke(this, EventArgs.Empty);
                        SaveWorkspace();
                        return true;
                    }

                case ".rcb":
                    {
                        // Solo la zona central puede abrir Carts.
                        if (Zone != TabZone.Center) return false;
                        if (CartBoardFactory is null) return false;

                        var vm = CartBoardFactory();
                        if (vm is null) return false;
                        if (!vm.LoadFromFile(path)) return false;

                        var tab = CreateCartTab(vm);
                        Tabs.Add(tab);
                        SelectedTab = tab;
                        TabsChanged?.Invoke(this, EventArgs.Empty);
                        SaveWorkspace();
                        return true;
                    }

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
