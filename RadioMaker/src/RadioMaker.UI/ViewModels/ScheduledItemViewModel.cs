using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

public sealed partial class ScheduledItemViewModel : ViewModelBase, IDisposable
{
    private readonly ScheduledItem _item;

    private readonly EventHandler _themeChangedHandler;
    private bool _disposed;

    // Cache de informacion de la playlist asociada (solo si Kind == Playlist).
    private int _playlistEnabledTrackCount;
    private string _playlistDisplayName = string.Empty;
    private bool _playlistLoadFailed;

    // Cache de informacion de la tanda asociada (solo si Kind == Tanda).
    private int _tandaTrackCount;
    private string _tandaDisplayName = string.Empty;
    private bool _tandaLoadFailed;

    public ScheduledItem Source => _item;

    public event EventHandler? Changed;

    // ───── Propiedades observables ───────────────────────────────────────

    [ObservableProperty] private ScheduledItemKind _kind;
    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _title;
    [ObservableProperty] private SchedulePriority _priority;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _notes;

    [ObservableProperty] private bool _isPlayed;
    [ObservableProperty] private bool _isCurrentlyPlaying;

    [ObservableProperty] private string _blockName = "Nuevo Bloque";

    public ObservableCollection<ScheduleEntryViewModel> Entries { get; } = new();
    public ObservableCollection<TandaTrackViewModel> TandaTracks { get; } = new();
    public ObservableCollection<ScheduledBlockItemViewModel> BlockItems { get; } = new();

    public ScheduledItemKind[] AvailableKinds { get; } =
        { ScheduledItemKind.SingleFile, ScheduledItemKind.Tanda, ScheduledItemKind.Playlist, ScheduledItemKind.Block };

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : (Kind == ScheduledItemKind.Tanda
                ? (!string.IsNullOrWhiteSpace(FilePath)
                    ? Path.GetFileNameWithoutExtension(FilePath)
                    : (_item.EmbeddedTanda?.DisplayName ?? "(Tanda sin archivo)"))
                : Kind == ScheduledItemKind.Block
                    ? (_item.EmbeddedBlock?.DisplayName ?? "(Bloque sin nombre)")
                    : Kind == ScheduledItemKind.Playlist
                        ? (!string.IsNullOrWhiteSpace(FilePath)
                            ? Path.GetFileNameWithoutExtension(FilePath)
                            : "(sin playlist)")
                        : (!string.IsNullOrWhiteSpace(FilePath)
                            ? Path.GetFileName(FilePath)
                            : "(sin archivo)"));

    public string PriorityText => Priority == SchedulePriority.High ? "ALTA" : "Baja";

    public string PriorityColor
    {
        get
        {
            if (IsCurrentlyPlaying) return "Error";
            if (IsPlayed) return "FgSubtle";
            return Priority == SchedulePriority.High ? "Warning" : "FgMuted";
        }
    }

    public string KindText => Kind switch
    {
        ScheduledItemKind.Tanda => "TANDA",
        ScheduledItemKind.Playlist => "PLAYLIST",
        ScheduledItemKind.Block => "BLOQUE",
        _ => "Archivo"
    };

    public string KindColor => Kind switch
    {
        ScheduledItemKind.Tanda => "Accent",
        ScheduledItemKind.Playlist => "Success",
        ScheduledItemKind.Block => "Warning",
        _ => "FgMuted"
    };

    public bool IsSingleFile => Kind == ScheduledItemKind.SingleFile;
    public bool IsTanda => Kind == ScheduledItemKind.Tanda;
    public bool IsPlaylist => Kind == ScheduledItemKind.Playlist;
    public bool IsBlock => Kind == ScheduledItemKind.Block;

    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => !HasEntries;
    public bool HasTandaTracks => TandaTracks.Count > 0;
    public bool HasNoTandaTracks => !HasTandaTracks;
    public bool HasBlockItems => BlockItems.Count > 0;
    public bool HasNoBlockItems => !HasBlockItems;

    // ===== INTEGRACIÓN RMWave =====
    /// -------------True si el FilePath del item es un .rmw.</summary>
    public bool IsRmwProject =>
        !string.IsNullOrWhiteSpace(FilePath)
        && FilePath.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);

    public string FileTypeBadge => IsRmwProject ? "PROYECTO RMWAVE" : string.Empty;
    public string FileTypeBadgeColor => IsRmwProject ? "Accent" : "Transparent";

    public bool HasContent => Kind switch
    {
        ScheduledItemKind.SingleFile => !string.IsNullOrWhiteSpace(FilePath),
        ScheduledItemKind.Tanda => !string.IsNullOrWhiteSpace(FilePath) || TandaTracks.Count > 0,
        ScheduledItemKind.Playlist => !string.IsNullOrWhiteSpace(FilePath),
        ScheduledItemKind.Block => BlockItems.Count > 0,
        _ => false
    };

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

    // ───── Totalizador de duracion ──────────────────────────────────────

    public double BlockTotalDurationSeconds
    {
        get
        {
            double total = 0;
            foreach (var bi in BlockItems)
                total += bi.DurationSeconds;
            return total;
        }
    }

    public string BlockTotalDurationText
    {
        get
        {
            if (BlockItems.Count == 0) return string.Empty;

            double total = BlockTotalDurationSeconds;
            if (total <= 0.5) return "—";

            return AudioFileInfoReader.FormatDuration(total);
        }
    }

    public double TotalDurationSeconds
    {
        get
        {
            if (Kind == ScheduledItemKind.SingleFile
                || Kind == ScheduledItemKind.Playlist
                || Kind == ScheduledItemKind.Tanda)
                return _item.DurationSeconds;

            if (Kind == ScheduledItemKind.Block)
                return BlockTotalDurationSeconds;

            // Fallback retrocompat: Tanda embebida (formato viejo).
            double total = 0;
            foreach (var t in TandaTracks.Where(t => t.IsEnabled))
            {
                total += t.DurationSeconds + t.DelayBeforeSeconds;
            }
            return total;
        }
    }

    public string DurationText
    {
        get
        {
            if (Kind == ScheduledItemKind.Tanda && string.IsNullOrWhiteSpace(FilePath) && TandaTracks.Count == 0)
                return "Sin tanda";

            if (Kind == ScheduledItemKind.Block && BlockItems.Count == 0)
                return "Bloque vacio";

            if (Kind == ScheduledItemKind.Playlist && string.IsNullOrWhiteSpace(FilePath))
                return "Sin playlist";

            return $"Duracion: {AudioFileInfoReader.FormatDuration(TotalDurationSeconds)}";
        }
    }

    public string PathText
    {
        get
        {
            if (Kind == ScheduledItemKind.SingleFile)
                return string.IsNullOrWhiteSpace(FilePath) ? string.Empty : FilePath;

            if (Kind == ScheduledItemKind.Playlist)
            {
                if (string.IsNullOrWhiteSpace(FilePath))
                    return "(sin playlist)";

                if (_playlistLoadFailed)
                    return $"{Path.GetFileName(FilePath)}  (no se pudo cargar)";

                if (_playlistEnabledTrackCount == 0 && string.IsNullOrEmpty(_playlistDisplayName))
                    return Path.GetFileName(FilePath);

                var name = string.IsNullOrWhiteSpace(_playlistDisplayName)
                    ? Path.GetFileNameWithoutExtension(FilePath)
                    : _playlistDisplayName;

                return $"{name} — {_playlistEnabledTrackCount} pista(s)";
            }

            if (Kind == ScheduledItemKind.Tanda)
            {
                // Nuevo modelo: referencia al .tnd.
                if (!string.IsNullOrWhiteSpace(FilePath))
                {
                    if (_tandaLoadFailed)
                        return $"{Path.GetFileName(FilePath)}  (no se pudo cargar)";

                    if (_tandaTrackCount == 0 && string.IsNullOrEmpty(_tandaDisplayName))
                        return Path.GetFileName(FilePath);

                    var name = string.IsNullOrWhiteSpace(_tandaDisplayName)
                        ? Path.GetFileNameWithoutExtension(FilePath)
                        : _tandaDisplayName;

                    return $"{name} — {_tandaTrackCount} pista(s)";
                }

                // Retrocompat: tanda embebida (formato antiguo, sin .tnd).
                if (TandaTracks.Count > 0)
                {
                    int cnt = TandaTracks.Count;
                    int enabled = TandaTracks.Count(t => t.IsEnabled);
                    return $"(tanda embebida, formato antiguo) {cnt} pista(s) — {enabled} activa(s)";
                }

                return "(sin tanda)";
            }

            if (Kind == ScheduledItemKind.Block)
            {
                if (BlockItems.Count == 0)
                    return "(bloque vacio)";

                int playlists = BlockItems.Count(i => i.Kind == ScheduledItemKind.Playlist);
                int tandas = BlockItems.Count(i => i.Kind == ScheduledItemKind.Tanda);
                int archivos = BlockItems.Count(i => i.Kind == ScheduledItemKind.SingleFile);

                var parts = new List<string>();
                if (playlists > 0) parts.Add($"{playlists} playlist(s)");
                if (tandas > 0) parts.Add($"{tandas} tanda(s)");
                if (archivos > 0) parts.Add($"{archivos} archivo(s)");

                return string.Join(", ", parts);
            }

            return string.Empty;
        }
    }

    public string EntriesSummary
    {
        get
        {
            if (Entries.Count == 0) return "Sin horarios programados";
            if (Entries.Count == 1) return Entries[0].ShortDescription;
            return $"{Entries.Count} horarios: " + string.Join(" · ", Entries.Take(3).Select(e => e.ShortDescription))
                   + (Entries.Count > 3 ? " ..." : string.Empty);
        }
    }

    /// -------------
    /// Resumen del horario configurado y de la próxima salida. La salida se
    /// calcula entre todas las entradas habilitadas usando la hora local.
    /// -------------
    public string ScheduleSummaryText
    {
        get
        {
            var activeEntries = Entries
                .Where(e => e.IsEnabled && e.Source.Days is { Count: > 0 })
                .ToList();
            if (activeEntries.Count == 0) return "Sin horarios activos";

            var dayNames = new Dictionary<DayOfWeek, string>
            {
                [DayOfWeek.Monday] = "Lun", [DayOfWeek.Tuesday] = "Mar",
                [DayOfWeek.Wednesday] = "Mié", [DayOfWeek.Thursday] = "Jue",
                [DayOfWeek.Friday] = "Vie", [DayOfWeek.Saturday] = "Sáb",
                [DayOfWeek.Sunday] = "Dom"
            };
            var days = activeEntries
                .SelectMany(e => e.Source.Days)
                .Distinct()
                .OrderBy(day => ((int)day + 6) % 7)
                .Select(day => dayNames.GetValueOrDefault(day, day.ToString()));
            var daysText = string.Join("/", days);

            var now = DateTime.Now;
            DateTime? next = null;
            foreach (var entry in activeEntries)
            {
                foreach (var day in entry.Source.Days)
                {
                    for (var offset = 0; offset <= 7; offset++)
                    {
                        var date = now.Date.AddDays(offset);
                        if (date.DayOfWeek != day) continue;

                        var occurrence = date.AddHours(entry.Hour).AddMinutes(entry.Minute);
                        if (occurrence <= now) continue;
                        if (next is null || occurrence < next.Value) next = occurrence;
                        break;
                    }
                }
            }

            if (next is null) return $"Días: {daysText}  ·  Sin próxima salida";

            var duration = TotalDurationSeconds;
            var endText = "--:--";
            if (duration > 0.5)
            {
                var end = next.Value.AddSeconds(duration);
                var dayOffset = (end.Date - next.Value.Date).Days;
                endText = dayOffset > 0
                    ? $"{end:HH:mm} (+{dayOffset}d)"
                    : end.ToString("HH:mm");
            }

            return $"Días: {daysText}  ·  Sale: {next.Value:HH:mm}  ·  Fin: {endText}";
        }
    }

    /// -------------Refresca el texto horario cuando cambia el reloj.</summary>
    public void RefreshScheduleDisplay() => OnPropertyChanged(nameof(ScheduleSummaryText));

    public string TandaName
    {
        get => _item.EmbeddedTanda?.Name ?? "Nueva Tanda";
        set
        {
            if (_item.EmbeddedTanda is null) return;
            if (_item.EmbeddedTanda.Name == value) return;
            _item.EmbeddedTanda.Name = value ?? string.Empty;
            OnPropertyChanged(nameof(TandaName));
            OnPropertyChanged(nameof(DisplayTitle));
            NotifyChanged();
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public ScheduledItemViewModel(ScheduledItem item)
    {
        _item = item;

        _kind = item.Kind;
        _filePath = item.FilePath;
        _title = item.Title;
        _priority = item.Priority;
        _isEnabled = item.IsEnabled;
        _notes = item.Notes;
        _blockName = item.EmbeddedBlock?.Name ?? "Nuevo Bloque";

        foreach (var entry in item.Entries)
        {
            var vm = new ScheduleEntryViewModel(entry);
            vm.Changed += OnEntryChanged;
            Entries.Add(vm);
        }

        if (item.EmbeddedTanda is not null)
        {
            foreach (var track in item.EmbeddedTanda.Tracks)
            {
                var vm = new TandaTrackViewModel(track);
                vm.Changed += OnTandaTrackChanged;
                TandaTracks.Add(vm);
            }
        }

        if (item.EmbeddedBlock is not null)
        {
            foreach (var blockItem in item.EmbeddedBlock.Items)
            {
                var vm = new ScheduledBlockItemViewModel(blockItem);
                vm.Changed += OnBlockItemChanged;
                BlockItems.Add(vm);
            }
        }

        // Refrescar duraciones / info al construir el VM.
        if (item.Kind == ScheduledItemKind.SingleFile &&
            item.DurationSeconds <= 0 &&
            !string.IsNullOrWhiteSpace(item.FilePath))
        {
            _ = RefreshSingleFileDurationAsync(item.FilePath);
        }
        else if (item.Kind == ScheduledItemKind.Playlist &&
                 !string.IsNullOrWhiteSpace(item.FilePath))
        {
            _ = RefreshPlaylistInfoAsync(item.FilePath);
        }
        else if (item.Kind == ScheduledItemKind.Tanda &&
                 !string.IsNullOrWhiteSpace(item.FilePath))
        {
            _ = RefreshTandaInfoAsync(item.FilePath);
        }
        else if (item.Kind == ScheduledItemKind.Block)
        {
            _item.DurationSeconds = BlockTotalDurationSeconds;
        }

        _themeChangedHandler = OnThemeChanged;
        RadioMaker.UI.Services.ThemeService.ThemeChanged += _themeChangedHandler;
    }

    // ───── Lectura de duracion (SingleFile) ──────────────────────────────

    private async Task RefreshSingleFileDurationAsync(string path)
    {
        double seconds = 0;
        try
        {
            seconds = await Task.Run(() => AudioFileInfoReader.GetDurationSeconds(path));
        }
        catch { }

        if (!string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase)) return;

        double previous = _item.DurationSeconds;
        _item.DurationSeconds = seconds;

        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));

        // Solo notificar si el valor cambio. Si es el mismo que ya estaba
        // cacheado (caso tipico al reabrir la app), NO marcamos Changed
        // para no ensuciar el IsDirty del ScheduledListViewModel.
        if (Math.Abs(previous - seconds) > 0.01)
            NotifyChanged();
    }

    // ───── Carga de info y duracion (Playlist) ──────────────────────────

    private async Task RefreshPlaylistInfoAsync(string rmfPath)
    {
        Playlist? playlist = null;
        try { playlist = await Task.Run(() => PlaylistStore.Load(rmfPath)); } catch { }

        if (playlist is null)
        {
            if (!string.Equals(FilePath, rmfPath, StringComparison.OrdinalIgnoreCase)) return;

            _playlistLoadFailed = true;
            _playlistEnabledTrackCount = 0;
            _playlistDisplayName = string.Empty;

            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(DurationText));
            NotifyChanged();
            return;
        }

        if (!string.Equals(FilePath, rmfPath, StringComparison.OrdinalIgnoreCase)) return;

        _playlistLoadFailed = false;
        _playlistDisplayName = playlist.DisplayName;
        _playlistEnabledTrackCount = playlist.EnabledTrackCount;

        OnPropertyChanged(nameof(PathText));

        double total = 0;
        foreach (var pt in playlist.Tracks.Where(t => t.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(pt.FilePath)) continue;

            double dur = pt.DurationSeconds;
            if (dur <= 0.5)
            {
                try { dur = await Task.Run(() => AudioFileInfoReader.GetDurationSeconds(pt.FilePath)); }
                catch { dur = 0; }
            }
            total += dur;
        }

        if (!string.Equals(FilePath, rmfPath, StringComparison.OrdinalIgnoreCase)) return;

        double previous = _item.DurationSeconds;
        _item.DurationSeconds = total;

        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));

        // Solo notificar si el valor cambio (evita marcar IsDirty al
        // reabrir la app cuando la duracion cacheada coincide con la
        // recien calculada).
        if (Math.Abs(previous - total) > 0.01)
            NotifyChanged();
    }

    // ───── Carga de info y duracion (Tanda .tnd) ───────────────────────

    private async Task RefreshTandaInfoAsync(string tndPath)
    {
        Tanda? tanda = null;
        try { tanda = await Task.Run(() => TandaStore.Load(tndPath)); } catch { }

        if (tanda is null)
        {
            if (!string.Equals(FilePath, tndPath, StringComparison.OrdinalIgnoreCase)) return;

            _tandaLoadFailed = true;
            _tandaTrackCount = 0;
            _tandaDisplayName = string.Empty;

            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(DurationText));
            NotifyChanged();
            return;
        }

        if (!string.Equals(FilePath, tndPath, StringComparison.OrdinalIgnoreCase)) return;

        _tandaLoadFailed = false;
        _tandaDisplayName = tanda.DisplayName;
        _tandaTrackCount = tanda.EnabledTrackCount;

        OnPropertyChanged(nameof(PathText));

        double total = 0;
        foreach (var t in tanda.Tracks.Where(t => t.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(t.FilePath)) continue;

            double dur = t.DurationSeconds;
            if (dur <= 0.5)
            {
                try { dur = await Task.Run(() => AudioFileInfoReader.GetDurationSeconds(t.FilePath)); }
                catch { dur = 0; }
            }
            total += dur;
            total += t.DelayBeforeSeconds;
        }

        if (!string.Equals(FilePath, tndPath, StringComparison.OrdinalIgnoreCase)) return;

        double previous = _item.DurationSeconds;
        _item.DurationSeconds = total;

        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));

        // Solo notificar si el valor cambio (evita marcar IsDirty al
        // reabrir la app cuando la duracion cacheada coincide con la
        // recien calculada).
        if (Math.Abs(previous - total) > 0.01)
            NotifyChanged();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(PriorityColor));
            OnPropertyChanged(nameof(KindColor));
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

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private void AddEntry()
    {
        var entry = new ScheduleEntry
        {
            Hour = 12,
            Minute = 0,
            IsEnabled = true,
            Days = new List<DayOfWeek> { DayOfWeek.Monday }
        };

        var vm = new ScheduleEntryViewModel(entry);
        vm.Changed += OnEntryChanged;
        Entries.Add(vm);

        ApplyToModel();
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(EntriesSummary));
        NotifyChanged();
    }

    [RelayCommand]
    private void RemoveEntry(ScheduleEntryViewModel? entryVm)
    {
        if (entryVm is null) return;
        if (!Entries.Contains(entryVm)) return;

        entryVm.Changed -= OnEntryChanged;
        Entries.Remove(entryVm);

        ApplyToModel();
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(EntriesSummary));
        NotifyChanged();
    }

    // NOTA: los comandos AddTandaTrack / RemoveTandaTrack / MoveTrackUp /
    // MoveTrackDown se conservan por retrocompatibilidad con .rms viejos
    // que todavia tengan una Tanda embebida. Ya no se exponen en el editor.

    [RelayCommand]
    private void AddTandaTrack()
    {
        EnsureEmbeddedTanda();

        var track = new TandaTrack { IsEnabled = true };
        var vm = new TandaTrackViewModel(track);
        vm.Changed += OnTandaTrackChanged;
        TandaTracks.Add(vm);
        _item.EmbeddedTanda!.Tracks.Add(track);

        ApplyToModel();
        OnPropertyChanged(nameof(HasTandaTracks));
        OnPropertyChanged(nameof(HasNoTandaTracks));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
    }

    [RelayCommand]
    private void RemoveTandaTrack(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        if (!TandaTracks.Contains(trackVm)) return;

        trackVm.Changed -= OnTandaTrackChanged;
        TandaTracks.Remove(trackVm);

        if (_item.EmbeddedTanda is not null)
            _item.EmbeddedTanda.Tracks.Remove(trackVm.Source);

        ApplyToModel();
        OnPropertyChanged(nameof(HasTandaTracks));
        OnPropertyChanged(nameof(HasNoTandaTracks));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
    }

    public TandaTrackViewModel AddTrackFromFile(string filePath)
    {
        EnsureEmbeddedTanda();

        var track = new TandaTrack
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            IsEnabled = true
        };

        var vm = new TandaTrackViewModel(track);
        vm.Changed += OnTandaTrackChanged;
        TandaTracks.Add(vm);
        _item.EmbeddedTanda!.Tracks.Add(track);

        ApplyToModel();
        OnPropertyChanged(nameof(HasTandaTracks));
        OnPropertyChanged(nameof(HasNoTandaTracks));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
        return vm;
    }

    [RelayCommand]
    private void MoveTrackUp(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = TandaTracks.IndexOf(trackVm);
        if (idx <= 0) return;
        TandaTracks.Move(idx, idx - 1);
        ApplyTandaOrderToModel();
        NotifyChanged();
    }

    [RelayCommand]
    private void MoveTrackDown(TandaTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = TandaTracks.IndexOf(trackVm);
        if (idx < 0 || idx >= TandaTracks.Count - 1) return;
        TandaTracks.Move(idx, idx + 1);
        ApplyTandaOrderToModel();
        NotifyChanged();
    }

    // ───── Comandos: items del bloque ───────────────────────────────────

    [RelayCommand]
    private void AddBlockItemPlaylist()
    {
        EnsureEmbeddedBlock();
        AddBlockItemInternal(ScheduledItemKind.Playlist);
    }

    [RelayCommand]
    private void AddBlockItemTanda()
    {
        EnsureEmbeddedBlock();
        AddBlockItemInternal(ScheduledItemKind.Tanda);
    }

    [RelayCommand]
    private void AddBlockItemFile()
    {
        EnsureEmbeddedBlock();
        AddBlockItemInternal(ScheduledItemKind.SingleFile);
    }

    private void AddBlockItemInternal(ScheduledItemKind kind)
    {
        if (_item.EmbeddedBlock is null) return;

        var blockItem = new ScheduledBlockItem { Kind = kind };
        var vm = new ScheduledBlockItemViewModel(blockItem);
        vm.Changed += OnBlockItemChanged;
        BlockItems.Add(vm);
        _item.EmbeddedBlock.Items.Add(blockItem);

        ApplyToModel();
        OnPropertyChanged(nameof(HasBlockItems));
        OnPropertyChanged(nameof(HasNoBlockItems));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        NotifyChanged();
    }

    [RelayCommand]
    private void RemoveBlockItem(ScheduledBlockItemViewModel? itemVm)
    {
        if (itemVm is null) return;
        if (!BlockItems.Contains(itemVm)) return;

        itemVm.Changed -= OnBlockItemChanged;
        BlockItems.Remove(itemVm);

        if (_item.EmbeddedBlock is not null)
            _item.EmbeddedBlock.Items.Remove(itemVm.Source);

        _item.DurationSeconds = BlockTotalDurationSeconds;

        ApplyToModel();
        OnPropertyChanged(nameof(HasBlockItems));
        OnPropertyChanged(nameof(HasNoBlockItems));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        NotifyChanged();
    }

    [RelayCommand]
    private void MoveBlockItemUp(ScheduledBlockItemViewModel? itemVm)
    {
        if (itemVm is null) return;
        int idx = BlockItems.IndexOf(itemVm);
        if (idx <= 0) return;
        BlockItems.Move(idx, idx - 1);
        ApplyBlockOrderToModel();
        NotifyChanged();
    }

    [RelayCommand]
    private void MoveBlockItemDown(ScheduledBlockItemViewModel? itemVm)
    {
        if (itemVm is null) return;
        int idx = BlockItems.IndexOf(itemVm);
        if (idx < 0 || idx >= BlockItems.Count - 1) return;
        BlockItems.Move(idx, idx + 1);
        ApplyBlockOrderToModel();
        NotifyChanged();
    }

    // ───── Converters de tipo ────────────────────────────────────────────

    [RelayCommand]
    private void ConvertToTanda()
    {
        if (Kind == ScheduledItemKind.Tanda) return;

        // Limpiar FilePath si tenia una extension que no aplica (.rmf).
        if (!string.IsNullOrWhiteSpace(FilePath) &&
            FilePath.EndsWith(".rmf", StringComparison.OrdinalIgnoreCase))
        {
            FilePath = string.Empty;
        }

        // El nuevo modelo de Tanda usa un .tnd referenciado.
        // NO creamos tracks embebidas. La tanda se arma en su propia pestaña.

        _tandaLoadFailed = false;
        _tandaTrackCount = 0;
        _tandaDisplayName = string.Empty;

        Kind = ScheduledItemKind.Tanda;
        _item.DurationSeconds = 0;

        ApplyToModel();
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        NotifyChanged();
    }

    [RelayCommand]
    private void ConvertToSingleFile()
    {
        if (Kind == ScheduledItemKind.SingleFile) return;

        if (!string.IsNullOrWhiteSpace(FilePath) &&
            (FilePath.EndsWith(".rmf", StringComparison.OrdinalIgnoreCase) ||
             FilePath.EndsWith(".tnd", StringComparison.OrdinalIgnoreCase)))
        {
            FilePath = string.Empty;
        }

        Kind = ScheduledItemKind.SingleFile;
        ApplyToModel();
        NotifyChanged();
    }

    [RelayCommand]
    private void ConvertToPlaylist()
    {
        if (Kind == ScheduledItemKind.Playlist) return;

        if (!string.IsNullOrWhiteSpace(FilePath) &&
            !FilePath.EndsWith(".rmf", StringComparison.OrdinalIgnoreCase))
        {
            FilePath = string.Empty;
        }

        _playlistLoadFailed = false;
        _playlistEnabledTrackCount = 0;
        _playlistDisplayName = string.Empty;

        Kind = ScheduledItemKind.Playlist;
        _item.DurationSeconds = 0;

        ApplyToModel();
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        NotifyChanged();
    }

    [RelayCommand]
    private void ConvertToBlock()
    {
        if (Kind == ScheduledItemKind.Block) return;

        EnsureEmbeddedBlock();

        if (!string.IsNullOrWhiteSpace(FilePath))
            FilePath = string.Empty;

        Kind = ScheduledItemKind.Block;
        ApplyToModel();
        NotifyChanged();
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    public void ApplyToModel()
    {
        _item.Kind = Kind;
        _item.FilePath = FilePath ?? string.Empty;
        _item.Title = Title ?? string.Empty;
        _item.Priority = Priority;
        _item.IsEnabled = IsEnabled;
        _item.Notes = Notes ?? string.Empty;

        _item.Entries.Clear();
        foreach (var entryVm in Entries)
        {
            entryVm.ApplyToModel();
            _item.Entries.Add(entryVm.Source);
        }

        if (_item.EmbeddedTanda is not null)
        {
            foreach (var trackVm in TandaTracks)
                trackVm.ApplyToModel();
        }

        if (_item.EmbeddedBlock is not null)
        {
            _item.EmbeddedBlock.Name = BlockName ?? "Nuevo Bloque";
            foreach (var itemVm in BlockItems)
                itemVm.ApplyToModel();
        }
    }

    public void ReplaceEmbeddedTanda(Tanda tanda)
    {
        if (tanda is null) return;

        _item.EmbeddedTanda = tanda;

        foreach (var vm in TandaTracks)
            vm.Changed -= OnTandaTrackChanged;
        TandaTracks.Clear();

        foreach (var track in tanda.Tracks)
        {
            var vm = new TandaTrackViewModel(track);
            vm.Changed += OnTandaTrackChanged;
            TandaTracks.Add(vm);
        }

        if (Kind != ScheduledItemKind.Tanda)
            Kind = ScheduledItemKind.Tanda;

        ApplyToModel();

        OnPropertyChanged(nameof(TandaName));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(HasTandaTracks));
        OnPropertyChanged(nameof(HasNoTandaTracks));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
    }

    public void CopyFrom(ScheduledItemViewModel other)
    {
        if (other is null) return;

        foreach (var e in Entries) e.Changed -= OnEntryChanged;
        foreach (var t in TandaTracks) t.Changed -= OnTandaTrackChanged;
        foreach (var b in BlockItems) b.Changed -= OnBlockItemChanged;

        Kind = other.Kind;
        FilePath = other.FilePath;
        Title = other.Title;
        Priority = other.Priority;
        IsEnabled = other.IsEnabled;
        Notes = other.Notes;

        _item.DurationSeconds = other.Source.DurationSeconds;

        _item.EmbeddedTanda = other.Source.EmbeddedTanda;
        _item.EmbeddedBlock = other.Source.EmbeddedBlock;

        Entries.Clear();
        foreach (var entryVm in other.Entries)
        {
            var vm = new ScheduleEntryViewModel(entryVm.Source);
            vm.Changed += OnEntryChanged;
            Entries.Add(vm);
        }

        TandaTracks.Clear();
        foreach (var trackVm in other.TandaTracks)
        {
            var vm = new TandaTrackViewModel(trackVm.Source);
            vm.Changed += OnTandaTrackChanged;
            TandaTracks.Add(vm);
        }

        BlockItems.Clear();
        foreach (var blockItemVm in other.BlockItems)
        {
            var vm = new ScheduledBlockItemViewModel(blockItemVm.Source);
            vm.Changed += OnBlockItemChanged;
            BlockItems.Add(vm);
        }

        BlockName = other.BlockName;

        // Re-disparar cache de info si corresponde.
        if (Kind == ScheduledItemKind.Tanda && !string.IsNullOrWhiteSpace(FilePath))
            _ = RefreshTandaInfoAsync(FilePath);
        else if (Kind == ScheduledItemKind.Playlist && !string.IsNullOrWhiteSpace(FilePath))
            _ = RefreshPlaylistInfoAsync(FilePath);

        ApplyToModel();

        OnPropertyChanged(nameof(IsSingleFile));
        OnPropertyChanged(nameof(IsTanda));
        OnPropertyChanged(nameof(IsPlaylist));
        OnPropertyChanged(nameof(IsBlock));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(KindColor));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(EntriesSummary));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(HasTandaTracks));
        OnPropertyChanged(nameof(HasNoTandaTracks));
        OnPropertyChanged(nameof(HasBlockItems));
        OnPropertyChanged(nameof(HasNoBlockItems));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        NotifyChanged();
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void EnsureEmbeddedTanda()
    {
        if (_item.EmbeddedTanda is null)
        {
            _item.EmbeddedTanda = new Tanda
            {
                Name = !string.IsNullOrWhiteSpace(Title) ? Title : "Nueva Tanda",
                Tracks = new List<TandaTrack>()
            };
            OnPropertyChanged(nameof(TandaName));
        }
    }

    private void EnsureEmbeddedBlock()
    {
        if (_item.EmbeddedBlock is null)
        {
            _item.EmbeddedBlock = new ScheduledBlock
            {
                Name = !string.IsNullOrWhiteSpace(Title) ? Title : "Nuevo Bloque",
                Items = new List<ScheduledBlockItem>()
            };
            BlockName = _item.EmbeddedBlock.Name;
        }
    }

    private void ApplyTandaOrderToModel()
    {
        if (_item.EmbeddedTanda is null) return;
        _item.EmbeddedTanda.Tracks.Clear();
        foreach (var vm in TandaTracks)
        {
            vm.ApplyToModel();
            _item.EmbeddedTanda.Tracks.Add(vm.Source);
        }
    }

    private void ApplyBlockOrderToModel()
    {
        if (_item.EmbeddedBlock is null) return;
        _item.EmbeddedBlock.Items.Clear();
        foreach (var vm in BlockItems)
        {
            vm.ApplyToModel();
            _item.EmbeddedBlock.Items.Add(vm.Source);
        }
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnIsPlayedChanged(bool value)
    {
        OnPropertyChanged(nameof(PriorityColor));
        OnPropertyChanged(nameof(EffectiveOpacity));
        OnPropertyChanged(nameof(RowBackground));
    }

    partial void OnIsCurrentlyPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PriorityColor));
        OnPropertyChanged(nameof(EffectiveOpacity));
        OnPropertyChanged(nameof(RowBackground));
    }

    partial void OnBlockNameChanged(string value)
    {
        if (_item.EmbeddedBlock is not null)
            _item.EmbeddedBlock.Name = value ?? "Nuevo Bloque";
        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnKindChanged(ScheduledItemKind value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(IsSingleFile));
        OnPropertyChanged(nameof(IsTanda));
        OnPropertyChanged(nameof(IsPlaylist));
        OnPropertyChanged(nameof(IsBlock));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(KindColor));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        NotifyChanged();
    }

    partial void OnFilePathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(HasContent));

        if (Kind == ScheduledItemKind.SingleFile)
        {
            _item.DurationSeconds = 0;
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(DurationText));

            if (!string.IsNullOrWhiteSpace(value))
                _ = RefreshSingleFileDurationAsync(value);
        }
        else if (Kind == ScheduledItemKind.Playlist)
        {
            _item.DurationSeconds = 0;
            _playlistLoadFailed = false;
            _playlistEnabledTrackCount = 0;
            _playlistDisplayName = string.Empty;
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(DurationText));

            if (!string.IsNullOrWhiteSpace(value))
                _ = RefreshPlaylistInfoAsync(value);
        }
        else if (Kind == ScheduledItemKind.Tanda)
        {
            _item.DurationSeconds = 0;
            _tandaLoadFailed = false;
            _tandaTrackCount = 0;
            _tandaDisplayName = string.Empty;
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(DurationText));

            if (!string.IsNullOrWhiteSpace(value))
                _ = RefreshTandaInfoAsync(value);
        }

        // ===== INTEGRACIÓN RMWave =====
        OnPropertyChanged(nameof(IsRmwProject));
        OnPropertyChanged(nameof(FileTypeBadge));
        OnPropertyChanged(nameof(FileTypeBadgeColor));

        NotifyChanged();
    }

    partial void OnTitleChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnPriorityChanged(SchedulePriority value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(PriorityColor));
        NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(EffectiveOpacity));
        NotifyChanged();
    }

    partial void OnNotesChanged(string value) { ApplyToModel(); NotifyChanged(); }

    private void OnEntryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EntriesSummary));
        OnPropertyChanged(nameof(ScheduleSummaryText));
        NotifyChanged();
    }

    private void OnTandaTrackChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
    }

    private void OnBlockItemChanged(object? sender, EventArgs e)
    {
        _item.DurationSeconds = BlockTotalDurationSeconds;

        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PathText));
        NotifyChanged();
    }

    // ───── API para drag & drop desde el explorador ─────────────────────

    /// -------------
    /// Clasifica una extension en un ScheduledItemKind.
    /// Devuelve null si la extension no es compatible con un programado
    /// (por ejemplo .rms o .rcb, que no se pueden anidar).
    /// -------------
    public static ScheduledItemKind? DetectKindFromExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;

        if (string.Equals(ext, PlaylistStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return ScheduledItemKind.Playlist;

        if (string.Equals(ext, TandaStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return ScheduledItemKind.Tanda;

        if (string.Equals(ext, ScheduledListStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return null; // .rms no se puede anidar

        if (string.Equals(ext, CartBoardStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return null; // .rcb no se puede anidar

        // Cualquier otro archivo lo tratamos como audio.
        return ScheduledItemKind.SingleFile;
    }

    /// -------------
    /// Asigna un archivo al item respetando el tipo actual:
    ///   - Si es Playlist y el archivo es .rmf    -> asigna al FilePath.
    ///   - Si es Tanda    y el archivo es .tnd    -> asigna al FilePath.
    ///   - Si es SingleFile y el archivo es audio -> asigna al FilePath.
    ///   - Si el tipo no coincide, convierte automaticamente al tipo correcto.
    ///   - Si el archivo es .rms/.rcb, no hace nada.
    ///
    /// Devuelve true si algo se asigno.
    /// -------------
    public bool AssignFileFromPath(string path)
    {
        var detected = DetectKindFromExtension(path);
        if (detected is null) return false;

        // Si el tipo no coincide, convertir.
        switch (detected.Value)
        {
            case ScheduledItemKind.Playlist:
                if (Kind != ScheduledItemKind.Playlist)
                    ConvertToPlaylistCommand.Execute(null);
                FilePath = path;
                break;

            case ScheduledItemKind.Tanda:
                if (Kind != ScheduledItemKind.Tanda)
                    ConvertToTandaCommand.Execute(null);
                FilePath = path;
                break;

            case ScheduledItemKind.SingleFile:
                if (Kind != ScheduledItemKind.SingleFile)
                    ConvertToSingleFileCommand.Execute(null);
                FilePath = path;
                break;

            default:
                return false;
        }

        // Sugerir titulo si esta vacio.
        if (string.IsNullOrWhiteSpace(Title))
            Title = Path.GetFileNameWithoutExtension(path);

        return true;
    }

    /// -------------
    /// Agrega un item nuevo al bloque (si Kind == Block) a partir de un
    /// path. El tipo del BlockItem se infiere de la extension.
    ///
    /// Devuelve true si se agrego.
    /// -------------
    public bool AddBlockItemFromPath(string path)
    {
        if (Kind != ScheduledItemKind.Block) return false;

        var detected = DetectKindFromExtension(path);
        if (detected is null) return false;

        // Los bloques solo aceptan Playlist, Tanda y SingleFile.
        ScheduledItemKind blockKind = detected.Value;

        EnsureEmbeddedBlock();
        if (_item.EmbeddedBlock is null) return false;

        var blockItem = new ScheduledBlockItem
        {
            Kind = blockKind,
            FilePath = path,
            Title = Path.GetFileNameWithoutExtension(path)
        };

        var vm = new ScheduledBlockItemViewModel(blockItem);
        vm.Changed += OnBlockItemChanged;
        BlockItems.Add(vm);
        _item.EmbeddedBlock.Items.Add(blockItem);

        ApplyToModel();

        OnPropertyChanged(nameof(HasBlockItems));
        OnPropertyChanged(nameof(HasNoBlockItems));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(BlockTotalDurationSeconds));
        OnPropertyChanged(nameof(BlockTotalDurationText));
        NotifyChanged();

        return true;
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
