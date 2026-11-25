using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Wordprocessing;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del contenido de la pestaña Programados.
///
/// Soporta items de cuatro tipos:
///   - SingleFile: un archivo individual.
///   - Tanda: una tanda con multiples pistas embebidas.
///   - Playlist: referencia a un archivo .rmf ya armado en su propia pestaña.
///   - Block: un Bloque de Programacion con secuencia ordenada de items.
///
/// Persistencia: la lista completa se guarda en un .rms (JSON).
///
/// Reordenamiento: los items se pueden reordenar con los comandos
/// MoveItemUpCommand / MoveItemDownCommand.
/// -------------
public sealed partial class ScheduledListViewModel : ViewModelBase
{
    private readonly ScheduledList _list;
    private readonly RadioMaker.Core.Scheduling.PlaybackEngine? _playback;
    private string? _currentFilePath;
    private bool _suppressDirtyMark;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private string _listName;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.Initial");
    [ObservableProperty] private ScheduledItemViewModel? _selectedItem;
    [ObservableProperty] private ModuleBusSelectorViewModel? _busSelector;

    public ObservableCollection<ScheduledItemViewModel> Items { get; } = new();

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string TitleWithDirty => IsDirty ? ListName + " *" : ListName;

    public int ItemCount => Items.Count;
    public int EnabledCount => Items.Count(i => i.IsEnabled);
    public int HighPriorityCount => Items.Count(i => i.Priority == SchedulePriority.High);
    public int TandaCount => Items.Count(i => i.Kind == ScheduledItemKind.Tanda);
    public int PlaylistItemCount => Items.Count(i => i.Kind == ScheduledItemKind.Playlist);
    public int BlockCount => Items.Count(i => i.Kind == ScheduledItemKind.Block);

    /// -------------
    /// Se dispara cuando cambia IsEnabled. El TabHost lo usa para sincronizar
    /// el estado con el ScheduledListRegistry.
    /// -------------
    public event EventHandler? RegistryStateChanged;

    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => !HasItems;
    public bool HasSelection => SelectedItem is not null;
    public bool HasNoSelection => !HasSelection;

    public string SummaryText =>
        string.Format(LocalizationService.GetString("Loc.ScheduledList.Summary"), ItemCount, EnabledCount, HighPriorityCount, TandaCount, PlaylistItemCount, BlockCount);

    // ───── Totalizador de duracion ──────────────────────────────────────

    public double TotalDurationSeconds =>
        Items.Where(i => i.IsEnabled).Sum(i => i.TotalDurationSeconds);

    public int UnanalyzedItemCount =>
        Items.Count(i => i.IsEnabled
                         && i.HasContent
                         && i.TotalDurationSeconds <= 0.5);

    public string TotalDurationText
    {
        get
        {
            var total = TimeSpan.FromSeconds(TotalDurationSeconds);
            var baseText = $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}";

            int unanalyzed = UnanalyzedItemCount;
            return unanalyzed > 0
                ? string.Format(LocalizationService.GetString("Loc.ScheduledList.Duration.Unanalyzed"), baseText, unanalyzed)
                : baseText;
        }
    }

    // ───── Play manual ───────────────────────────────────────────────────

    /// -------------
    /// True si este VM inicio un Play manual y el engine todavia lo esta
    /// reproduciendo (o pausado, o suspendido por un programado ALTA).
    /// -------------
    [ObservableProperty] private bool _isManualPlaying;

    /// -------------
    /// Texto de estado del Play manual: "Reproduciendo item 3 de 12" o vacio.
    /// -------------
    [ObservableProperty] private string _manualPlaybackStatus = string.Empty;

    /// -------------
    /// True si la lista de ESTA pestaña es la que está sonando ahora
    /// (o la que tiene un item suspendido que se va a reanudar).
    ///
    /// Es la base para habilitar los botones de transporte: las otras
    /// pestañas de Programados abiertas deben mostrarlos deshabilitados.
    /// -------------
    public bool IsThisListActive
    {
        get
        {
            if (_playback is null) return false;

            var currentId = _playback.CurrentItemId;
            if (!string.IsNullOrEmpty(currentId) && ContainsItem(currentId))
                return true;

            var suspendedId = _playback.SuspendedItemId;
            if (!string.IsNullOrEmpty(suspendedId) && ContainsItem(suspendedId))
                return true;

            return false;
        }
    }

    /// -------------Se puede pausar: esta lista está activa y el canal está Playing.</summary>
    public bool CanPausePlayback =>
        IsThisListActive
        && _playback?.CurrentChannel?.State == RadioMaker.Core.Abstractions.PlaybackState.Playing;

    /// -------------Se puede reanudar: esta lista está activa y el canal está Paused.</summary>
    public bool CanResumePlayback =>
        IsThisListActive
        && _playback?.CurrentChannel?.State == RadioMaker.Core.Abstractions.PlaybackState.Paused;

    /// -------------Se puede retroceder: esta lista está activa.</summary>
    public bool CanPrevPlayback => IsThisListActive;

    /// -------------Se puede saltar al siguiente: esta lista está activa.</summary>
    public bool CanNextPlayback => IsThisListActive;

    /// -------------Se puede detener: esta lista está activa.</summary>
    public bool CanStopThisList => IsThisListActive;

    /// -------------
    /// Texto a mostrar en la barra de status. Prioriza el estado del
    /// Play manual cuando esta lista está activa; sino el StatusMessage normal.
    /// -------------
    public string DisplayStatusMessage =>
        !string.IsNullOrWhiteSpace(ManualPlaybackStatus)
            ? ManualPlaybackStatus
            : StatusMessage;

    /// -------------
    /// True si se puede iniciar un Play manual: hay engine y hay items
    /// habilitados.
    /// -------------
    public bool CanPlayList =>
        _playback is not null
        && Items.Any(i => i.IsEnabled)
        && !IsThisListActive;

    /// -------------Label del boton Play cuando ya esta en curso.</summary>
    public string PlayButtonTooltip =>
        IsManualPlaying
            ? LocalizationService.GetString("Loc.ScheduledList.Tooltip.Playing")
            : LocalizationService.GetString("Loc.ScheduledList.Tooltip.PlayAll");

    // =====================================================================
    // ───── CONSTRUCTOR ───────────────────────────────────────────────────
    // =====================================================================
    public ScheduledListViewModel()
        : this(new ScheduledList { Name = LocalizationService.GetString("Loc.ScheduledList.DefaultName") }, null)
    {
    }

    public ScheduledListViewModel(RadioMaker.Core.Scheduling.PlaybackEngine? playbackEngine)
        : this(new ScheduledList(), playbackEngine)
    {
    }

    public ScheduledListViewModel(
        ScheduledList list,
        RadioMaker.Core.Scheduling.PlaybackEngine? playbackEngine = null)
    {
        _list = list;
        _playback = playbackEngine;
        _listName = list.Name;
        _isEnabled = list.IsEnabled;

        foreach (var item in list.Items)
        {
            var vm = new ScheduledItemViewModel(item);
            vm.Changed += OnItemChanged;
            Items.Add(vm);
        }

        // Suscribirse al engine para reflejar el estado del Play manual.
        if (_playback is not null)
        {
            _playback.StateChanged += OnPlaybackStateChanged;
            _playback.ManualPlayFinished += OnManualPlayFinished;
        }
    }

    // ───── Comandos: agregar / quitar ────────────────────────────────────

    [RelayCommand]
    private void AddItem()
    {
        var item = new ScheduledItem
        {
            Kind = ScheduledItemKind.SingleFile,
            Title = LocalizationService.GetString("Loc.ScheduledList.NewItem"),
            Priority = SchedulePriority.Low,
            IsEnabled = true,
            Entries = new List<ScheduleEntry>
            {
                new()
                {
                    Hour = 12,
                    Minute = 0,
                    IsEnabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Monday }
                }
            }
        };

        AddItemInternal(item, LocalizationService.GetString("Loc.ScheduledList.Status.ItemAdded"));
    }

    [RelayCommand]
    private void AddTandaItem()
    {
        var item = new ScheduledItem
        {
            Kind = ScheduledItemKind.Tanda,
            Title = LocalizationService.GetString("Loc.ScheduledList.NewTanda"),
            Priority = SchedulePriority.Low,
            IsEnabled = true,
            Entries = new List<ScheduleEntry>
            {
                new()
                {
                    Hour = 12,
                    Minute = 0,
                    IsEnabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Monday }
                }
            }
        };

        AddItemInternal(item, LocalizationService.GetString("Loc.ScheduledList.Status.TandaAdded"));
    }

    [RelayCommand]
    private void AddPlaylistItem()
    {
        var item = new ScheduledItem
        {
            Kind = ScheduledItemKind.Playlist,
            Title = LocalizationService.GetString("Loc.ScheduledList.NewPlaylist"),
            Priority = SchedulePriority.Low,
            IsEnabled = true,
            Entries = new List<ScheduleEntry>
            {
                new()
                {
                    Hour = 12,
                    Minute = 0,
                    IsEnabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Monday }
                }
            }
        };

        AddItemInternal(item, LocalizationService.GetString("Loc.ScheduledList.Status.PlaylistAdded"));
    }

    [RelayCommand]
    private void AddBlockItem()
    {
        var item = new ScheduledItem
        {
            Kind = ScheduledItemKind.Block,
            Title = LocalizationService.GetString("Loc.ScheduledList.NewBlock"),
            Priority = SchedulePriority.Low,
            IsEnabled = true,
            EmbeddedBlock = new ScheduledBlock
            {
                Name = LocalizationService.GetString("Loc.ScheduledList.NewBlock"),
                Items = new List<ScheduledBlockItem>()
            },
            Entries = new List<ScheduleEntry>
            {
                new()
                {
                    Hour = 12,
                    Minute = 0,
                    IsEnabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Monday }
                }
            }
        };

        AddItemInternal(item, LocalizationService.GetString("Loc.ScheduledList.Status.BlockAdded"));
    }

    public SchedulePriority[] AvailablePriorities { get; } =
        { SchedulePriority.Low, SchedulePriority.High };

    private ScheduledItemViewModel AddItemInternal(ScheduledItem item, string message)
    {
        var vm = new ScheduledItemViewModel(item);
        vm.Changed += OnItemChanged;
        Items.Add(vm);
        _list.Items.Add(item);
        vm.ApplyToModel();

        SelectedItem = vm;
        IsDirty = true;
        StatusMessage = message;
        RefreshSummary();
        return vm;
    }

    [RelayCommand]
    private void RemoveSelectedItem()
    {
        if (SelectedItem is null) return;

        var vm = SelectedItem;
        vm.Changed -= OnItemChanged;
        try { vm.Dispose(); } catch { }
        Items.Remove(vm);
        _list.Items.Remove(vm.Source);

        SelectedItem = Items.FirstOrDefault();
        IsDirty = true;
        StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.ItemRemoved");
        RefreshSummary();
    }

    // ───── Reordenamiento de items ──────────────────────────────────────

    [RelayCommand]
    private void MoveItemUp(ScheduledItemViewModel? itemVm)
    {
        if (itemVm is null) return;
        int idx = Items.IndexOf(itemVm);
        if (idx <= 0) return;

        Items.Move(idx, idx - 1);
        SyncItemOrderToModel();
        IsDirty = true;
    }

    [RelayCommand]
    private void MoveItemDown(ScheduledItemViewModel? itemVm)
    {
        if (itemVm is null) return;
        int idx = Items.IndexOf(itemVm);
        if (idx < 0 || idx >= Items.Count - 1) return;

        Items.Move(idx, idx + 1);
        SyncItemOrderToModel();
        IsDirty = true;
    }

    /// -------------
    /// Inicia la reproduccion manual de toda la lista: reproduce los items
    /// habilitados en orden, sin esperar horarios, hasta terminar.
    /// Detiene lo que este sonando en este momento.
    /// -------------
    [RelayCommand]
    private void PlayList()
    {
        if (_playback is null)
        {
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.NoEngine");
            return;
        }

        if (!CanPlayList)
        {
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.NoEnabledItems");
            return;
        }

        // Snapshot de los items en el orden visual actual.
        var orderedItems = Items
            .Where(vm => vm.IsEnabled)
            .Select(vm =>
            {
                vm.ApplyToModel(); // por si hay cambios sin guardar
                return vm.Source;
            })
            .ToList();

        if (orderedItems.Count == 0)
        {
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.NoEnabledItems");
            return;
        }

        try
        {
            int enqueued = _playback.PlayList(orderedItems, ListName);

            if (enqueued == 0)
            {
                StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.PlayFailed");
                return;
            }

            IsManualPlaying = true;
            ManualPlaybackStatus = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.ManualProgress"), 1, enqueued);
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.ManualQueued"), enqueued);

            AppLogger.Info("PLAYLIST-MANUAL",
                $"Play manual iniciado en '{ListName}': {enqueued} item(s).");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.ManualPlayError"), ex.Message);
            AppLogger.Error("PLAYLIST-MANUAL", "Error en Play manual", ex);
        }
    }

    /// -------------
    /// Detiene la reproduccion manual en curso. No afecta items disparados
    /// por el scheduler.
    /// -------------
    [RelayCommand]
    private void StopPlayback()
    {
        if (_playback is null) return;

        try
        {
            _playback.StopAll();
            IsManualPlaying = false;
            ManualPlaybackStatus = string.Empty;
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.ManualStopped");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.StopError"), ex.Message);
        }
    }

    // =====================================================================
    // TRANSPORTE ADICIONAL: Pause / Resume / Next
    // =====================================================================

    [RelayCommand]
    private void PausePlayback()
    {
        if (_playback is null) return;
        _playback.Pause();
    }

    [RelayCommand]
    private void ResumePlayback()
    {
        if (_playback is null) return;
        _playback.Resume();
    }

    [RelayCommand]
    private void PrevPlayback()
    {
        if (_playback is null) return;
        _playback.Previous();
    }

    [RelayCommand]
    private void NextPlayback()
    {
        if (_playback is null) return;
        _playback.Next();
    }

    private void OnPlaybackStateChanged(object? sender, RadioMaker.Core.Abstractions.PlaybackStateInfo state)
    {
        if (_playback is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Actualizar el status text mientras haya un Play manual en curso.
            if (IsManualPlaying && state.IsActive)
            {
                if (state.IsTanda)
                    ManualPlaybackStatus = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.TrackProgress"), state.TandaTrackIndex + 1, state.TandaTrackCount, state.TandaTrackTitle);
                else
                    ManualPlaybackStatus = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.PlayingItem"), state.ItemTitle);
            }

            // Notificar que puede cambiar CanPlayList (por si cambio IsEnabled).
            OnPropertyChanged(nameof(CanPlayList));
            ManualPlaybackStatus = DisplayStatusMessage;

            // Notificar todas las propiedades de transporte.
            NotifyTransportStateChanged();
        });
    }

    private void NotifyTransportStateChanged()
    {
        OnPropertyChanged(nameof(IsThisListActive));
        OnPropertyChanged(nameof(CanPausePlayback));
        OnPropertyChanged(nameof(CanResumePlayback));
        OnPropertyChanged(nameof(CanNextPlayback));
        OnPropertyChanged(nameof(CanPrevPlayback));
        OnPropertyChanged(nameof(CanStopThisList));
        OnPropertyChanged(nameof(DisplayStatusMessage));
    }

    /// -------------
    /// El engine notifica que un Play manual termino (naturalmente o por
    /// StopAll). Recien aca apagamos IsManualPlaying.
    /// -------------
    private void OnManualPlayFinished(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsManualPlaying) return;

            IsManualPlaying = false;
            ManualPlaybackStatus = string.Empty;
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.ManualFinished");

            NotifyTransportStateChanged();
        });
    }

    private void SyncItemOrderToModel()
    {
        _list.Items.Clear();
        foreach (var vm in Items)
            _list.Items.Add(vm.Source);
    }

    // ───── Drag & drop / agregado por path ──────────────────────────────

    /// -------------
    /// Agrega un item nuevo a la lista a partir de un path soltado.
    /// El tipo se infiere de la extension:
    ///   - .rmf  -> nuevo item Playlist (con el .rmf como FilePath).
    ///   - .tnd  -> nuevo item Tanda   (con el .tnd como FilePath).
    ///   - audio -> nuevo item SingleFile.
    ///   - .rms / .rcb -> se ignora (no son anidables).
    /// Devuelve el item creado, o null si no se pudo.
    /// -------------
    public ScheduledItemViewModel? AddItemFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var kind = ScheduledItemViewModel.DetectKindFromExtension(path);
        if (kind is null)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.UnsupportedFile"), Path.GetFileName(path));
            return null;
        }

        var item = new ScheduledItem
        {
            Kind = kind.Value,
            FilePath = path,
            Title = Path.GetFileNameWithoutExtension(path),
            Priority = SchedulePriority.Low,
            IsEnabled = true,
            Entries = new List<ScheduleEntry>
            {
                new()
                {
                    Hour = 12,
                    Minute = 0,
                    IsEnabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Monday }
                }
            }
        };

        return AddItemInternal(item, string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.AddedFromBrowser"), Path.GetFileName(path)));
    }

    /// -------------
    /// Agrega un archivo individual como item nuevo (usado desde drag&drop
    /// del navegador interno). Delega en AddItemFromPath.
    /// -------------
    public ScheduledItemViewModel AddFileAsItem(string filePath)
    {
        return AddItemFromPath(filePath) ?? AddItemInternal(
            new ScheduledItem
            {
                Kind = ScheduledItemKind.SingleFile,
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Priority = SchedulePriority.Low,
                IsEnabled = true,
                Entries = new List<ScheduleEntry>
                {
                    new()
                    {
                        Hour = 12,
                        Minute = 0,
                        IsEnabled = true,
                        Days = new List<DayOfWeek> { DayOfWeek.Monday }
                    }
                }
            },
            string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.Added"), Path.GetFileName(filePath)));
    }

    // ───── Apertura / guardado .rms ──────────────────────────────────────

    public bool LoadFromFile(string filePath)
    {
        var loaded = ScheduledListStore.Load(filePath);
        if (loaded is null)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.LoadFailed"), Path.GetFileName(filePath));
            return false;
        }

        foreach (var vm in Items)
        {
            vm.Changed -= OnItemChanged;
            try { vm.Dispose(); } catch { }
        }
        Items.Clear();
        _list.Items.Clear();

        _list.Name = loaded.Name;
        _list.Version = loaded.Version;
        _list.CreatedAtUtc = loaded.CreatedAtUtc;
        _list.ModifiedAtUtc = loaded.ModifiedAtUtc;
        _list.IsEnabled = loaded.IsEnabled;

        ListName = loaded.Name;

        // Sincronizar sin marcar como dirty.
        _suppressDirtyMark = true;
        try { IsEnabled = loaded.IsEnabled; }
        finally { _suppressDirtyMark = false; }

        foreach (var item in loaded.Items)
        {
            _list.Items.Add(item);
            var vm = new ScheduledItemViewModel(item);
            vm.Changed += OnItemChanged;
            Items.Add(vm);
        }

        _currentFilePath = filePath;
        LoadedFilePath = filePath;
        IsDirty = false;

        SelectedItem = Items.FirstOrDefault();

        StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.Loaded"), Items.Count, Path.GetFileName(filePath));
        RefreshSummary();
        return true;
    }

    public bool SaveToFile(string? filePath = null)
    {
        var target = filePath ?? _currentFilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.NoSavePath");
            return false;
        }

        try
        {
            foreach (var vm in Items)
                vm.ApplyToModel();

            _list.Name = ListName;
            ScheduledListStore.Save(_list, target);

            if (!target.EndsWith(ScheduledListStore.FileExtension, StringComparison.OrdinalIgnoreCase))
                target += ScheduledListStore.FileExtension;

            _currentFilePath = target;
            LoadedFilePath = target;
            IsDirty = false;

            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.Saved"), Path.GetFileName(target));
            OnPropertyChanged(nameof(TitleWithDirty));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.ScheduledList.Status.SaveError"), ex.Message);
            return false;
        }
    }

    public void NewList()
    {
        foreach (var vm in Items)
        {
            vm.Changed -= OnItemChanged;
            try { vm.Dispose(); } catch { }
        }
        Items.Clear();
        _list.Items.Clear();

        _list.Name = LocalizationService.GetString("Loc.ScheduledList.DefaultName");
        _list.Version = 1;
        _list.CreatedAtUtc = DateTime.UtcNow;
        _list.ModifiedAtUtc = DateTime.UtcNow;
        _list.IsEnabled = true;

        ListName = _list.Name;

        _suppressDirtyMark = true;
        try { IsEnabled = true; }
        finally { _suppressDirtyMark = false; }

        _currentFilePath = null;
        LoadedFilePath = null;
        IsDirty = false;
        SelectedItem = null;

        StatusMessage = LocalizationService.GetString("Loc.ScheduledList.Status.NewList");
        RefreshSummary();
    }

    public ScheduledList GetInternalList() => _list;

    /// -------------
    /// Devuelve true si esta lista contiene un item con el Id indicado.
    /// Se usa para identificar si esta pestaña es la que esta sonando
    /// en el motor de reproduccion.
    /// -------------
    public bool ContainsItem(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;

        foreach (var vm in Items)
        {
            if (vm.Source.Id == itemId) return true;
        }
        return false;
    }

    public void ResetAllIsPlayed()
    {
        foreach (var item in Items)
        {
            item.IsPlayed = false;
            item.IsCurrentlyPlaying = false;
        }
    }

    public void MarkItemAsPlayed(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;

        foreach (var item in Items)
        {
            if (item.Source.Id == itemId)
            {
                item.IsPlayed = true;
                return;
            }
        }
    }

    public void SetCurrentlyPlaying(string? itemId)
    {
        foreach (var item in Items)
            item.IsCurrentlyPlaying = (item.Source.Id == itemId);
    }

    public void ClearCurrentlyPlaying()
    {
        foreach (var item in Items)
            item.IsCurrentlyPlaying = false;
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnListNameChanged(string value)
    {
        _list.Name = value ?? string.Empty;
        OnPropertyChanged(nameof(TitleWithDirty));
        IsDirty = true;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _list.IsEnabled = value;

        if (!_suppressDirtyMark)
            IsDirty = true;

        OnPropertyChanged(nameof(TitleWithDirty));
        try { RegistryStateChanged?.Invoke(this, EventArgs.Empty); } catch { }
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TitleWithDirty));

    partial void OnSelectedItemChanged(ScheduledItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }

    private void OnItemChanged(object? sender, EventArgs e)
    {
        if (sender is ScheduledItemViewModel item)
            item.RefreshScheduleDisplay();

        IsDirty = true;
        RefreshSummary();
        OnPropertyChanged(nameof(CanPlayList));
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(HighPriorityCount));
        OnPropertyChanged(nameof(TandaCount));
        OnPropertyChanged(nameof(PlaylistItemCount));
        OnPropertyChanged(nameof(BlockCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(SummaryText));

        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(UnanalyzedItemCount));
        OnPropertyChanged(nameof(TotalDurationText));
    }
}
