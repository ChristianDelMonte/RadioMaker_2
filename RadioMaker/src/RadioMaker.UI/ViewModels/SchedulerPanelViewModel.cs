using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Configuration;

using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel de control del scheduler.
/// Ademas de gestionar el scheduler, muestra el estado de reproduccion
/// actual que viene del PlaybackEngine.
/// -------------
public sealed partial class SchedulerPanelViewModel : ViewModelBase
{
    private readonly SchedulerService _service;
    private readonly PlaybackEngine? _playback;
    private readonly PlaylistPlayer? _playlistPlayer;

    private const int MaxVisibleLogEntries = 200;

    // ───── Estado del scheduler ──────────────────────────────────────────

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Scheduler detenido.";
    [ObservableProperty] private int _registeredListCount;
    [ObservableProperty] private int _enabledListCount;

    public ObservableCollection<SchedulerLogEntry> LogEntries { get; } = new();

    /// -------------
    /// Items programados que estan en cola esperando su turno de
    /// reproduccion (porque otro item esta sonando). Se ordenan por
    /// prioridad (ALTA primero) y luego por orden de llegada.
    /// -------------
    public ObservableCollection<QueuedItemViewModel> QueuedItems { get; } = new();

    // ───── Estado de reproduccion (viene del PlaybackEngine) ────────────
    [ObservableProperty] private PlaybackStateInfo _playbackState = PlaybackStateInfo.Idle;
    
    /// -------------Estado de la playlist (independiente del scheduler).</summary>
    [ObservableProperty] private PlaylistPlayerState _playlistState = PlaylistPlayerState.Stopped;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    /// -------------
    /// Registry del scheduler. Necesario para que EventsPanelViewModel
    /// consulte las listas activas y calcule los pendientes.
    /// -------------
    public ScheduledListRegistry Registry => _service.Registry;

    public string RegisteredText => $"{RegisteredListCount} lista(s) registrada(s)";
    public string EnabledText => $"{EnabledListCount} habilitada(s)";
    public string StateText => IsRunning ? "ACTIVO" : "Detenido";
    public string StateColor => IsRunning ? "#10B981" : "#6B7280";
    public bool HasLog => LogEntries.Count > 0;
    public bool HasNoLog => !HasLog;

    // ───── Panel de reproduccion actual (scheduler O playlist) ──────────

    /// -------------True si el scheduler esta reproduciendo algo ahora.</summary>
    public bool IsSchedulerActive => PlaybackState.IsActive;

    /// -------------True si la playlist esta sonando (reproduciendo o pausada).</summary>
    public bool IsPlaylistActive => PlaylistState.IsActive;

    /// -------------True si la playlist fue pausada por un programado.</summary>
    public bool IsPlaylistSuspended =>
        PlaylistState.Status == PlaylistPlayerStatus.PausedByScheduler;

    /// -------------
    /// True cuando no hay scheduler activo pero SI hay playlist sonando.
    /// En ese caso el panel de reproduccion muestra la info de la playlist.
    /// -------------
    public bool ShowPlaylistPanel => !IsSchedulerActive && IsPlaylistActive;

    /// -------------True cuando no hay nada sonando en ningun lado.</summary>
    public bool ShowEmptyState => !IsSchedulerActive && !IsPlaylistActive;

    // Textos del panel de playlist (para reusar el mismo layout)
    public string PlaylistHeaderText =>
        string.IsNullOrEmpty(PlaylistState.PlaylistName)
            ? "Playlist"
            : PlaylistState.PlaylistName;

    public string PlaylistTrackText => PlaylistState.TrackTitle;
    public string PlaylistProgressText => PlaylistState.TrackProgressText;
    public string PlaylistPositionText => PlaylistState.PositionText;
    public double PlaylistProgressFraction => PlaylistState.ProgressFraction;
    public string PlaylistStatusText => PlaylistState.StatusText;
    public string PlaylistStatusColorKey => PlaylistState.StatusColor;

    // ───── CONSTRUCTOR ───────────────────────────────────────────────────
    private readonly Avalonia.Threading.DispatcherTimer _queueRefreshTimer;
    public SchedulerPanelViewModel(SchedulerService service, PlaybackEngine? playback = null, PlaylistPlayer? playlistPlayer = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _playback = playback;
        _playlistPlayer = playlistPlayer;

        _service.LogAdded += OnLogAdded;
        _service.StateChanged += OnServiceStateChanged;

        if (_playback is not null)
        {
            _playback.StateChanged += OnPlaybackStateChanged;
        }

        // Escuchar tambien a la playlist: puede estar sonando en paralelo al
        // scheduler o suspendida por el mismo.
        if (_playlistPlayer is not null)
        {
            _playlistPlayer.StateChanged += OnPlaylistStateChanged;
        }

        ReloadLog();
        RefreshState();
        RefreshQueue();

        // Refrescar la cola cada 500ms (es barato: solo recorre la lista en
        // memoria del PlaybackEngine).
        _queueRefreshTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _queueRefreshTimer.Tick += (_, _) => RefreshQueue();
        _queueRefreshTimer.Start();
    }

    /// -------------
    /// Sincroniza la coleccion observable de QueuedItems con el estado
    /// actual del PlaybackEngine. Solo agrega/quita los que cambiaron
    /// para no regenerar la UI en cada tick.
    /// -------------
    private void RefreshQueue()
    {
        if (_playback is null) return;

        try
        {
            var snapshot = _playback.GetQueuedItems();

            // 1) Quitar los que ya no estan en el snapshot.
            for (int i = QueuedItems.Count - 1; i >= 0; i--)
            {
                var current = QueuedItems[i];
                bool stillThere = snapshot.Any(s =>
                    s.Title == current.Title &&
                    s.ListName == current.ListName);

                if (!stillThere)
                    QueuedItems.RemoveAt(i);
            }

            // 2) Agregar los nuevos items programados.
            foreach (var s in snapshot)
            {
                bool exists = QueuedItems.Any(q =>
                    q.Title == s.Title && q.ListName == s.ListName);

                if (!exists)
                {
                    QueuedItems.Add(new QueuedItemViewModel
                    {
                        Title = s.Title,
                        ListName = s.ListName,
                        Priority = s.Priority,
                        IsTanda = s.IsTanda,
                        TandaTrackCount = s.TandaTrackCount
                    });
                }
            }

            // 3) Sincronizar el item virtual de la playlist suspendida.
            SincronizarPlaylistEnCola();
        }
        catch { }
    }

    /// -------------
    /// Si la playlist esta pausada por un programado, agrega un item virtual
    /// al final de la cola para informarlo. Si ya no esta suspendida, lo quita.
    /// -------------
    private void SincronizarPlaylistEnCola()
    {
        bool suspended = IsPlaylistSuspended;
        var existing = QueuedItems.FirstOrDefault(q => q.IsSuspendedPlaylist);

        if (suspended && existing is null)
        {
            QueuedItems.Add(new QueuedItemViewModel
            {
                Title = PlaylistHeaderText,
                ListName = PlaylistTrackText,
                Priority = RadioMaker.Core.Scheduling.SchedulePriority.Low,
                IsSuspendedPlaylist = true
            });
        }
        else if (!suspended && existing is not null)
        {
            QueuedItems.Remove(existing);
        }
        else if (suspended && existing is not null)
        {
            // Actualizar titulo/pista en caso de que haya cambiado.
            existing.Title = PlaylistHeaderText;
            existing.ListName = PlaylistTrackText;
        }
    }

    // ───── Handlers de eventos (llegan desde el hilo del timer) ─────────

    private void OnLogAdded(object? sender, SchedulerLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Insert(0, entry);

            while (LogEntries.Count > MaxVisibleLogEntries)
                LogEntries.RemoveAt(LogEntries.Count - 1);

            OnPropertyChanged(nameof(HasLog));
            OnPropertyChanged(nameof(HasNoLog));
        });
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshState);
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateInfo state)
    {
        Dispatcher.UIThread.Post(() => PlaybackState = state);
    }

    private void OnPlaylistStateChanged(object? sender, PlaylistPlayerState state)
    {
        Dispatcher.UIThread.Post(() => PlaylistState = state);
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private void Start()
    {
        try
        {
            _service.Start();
            RefreshState();
        }
        catch (Exception ex)
        {
            StatusText = $"Error al iniciar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Stop()
    {
        try
        {
            _service.Stop();
            RefreshState();
        }
        catch (Exception ex)
        {
            StatusText = $"Error al detener: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleRun()
    {
        if (IsRunning) Stop();
        else Start();
    }

    [RelayCommand]
    private void ResetState()
    {
        try
        {
            _service.ResetState();
            _service.AddLog(SchedulerLogLevel.Info, "Estado de disparos reiniciado.");
            StatusText = "Estado de disparos reiniciado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void EvaluateNow()
    {
        try
        {
            _service.EvaluateNow();
            RefreshState();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
        OnPropertyChanged(nameof(HasLog));
        OnPropertyChanged(nameof(HasNoLog));
    }

    [RelayCommand]
    private void StopPlayback()
    {
        try
        {
            _playback?.StopAll();
            _service.AddLog(SchedulerLogLevel.Info, "Reproduccion programada detenida manualmente.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error al detener reproduccion: {ex.Message}";
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void ReloadLog()
    {
        LogEntries.Clear();

        var snapshot = _service.GetLogSnapshot();
        foreach (var entry in snapshot.Take(MaxVisibleLogEntries))
            LogEntries.Add(entry);

        OnPropertyChanged(nameof(HasLog));
        OnPropertyChanged(nameof(HasNoLog));
    }

    private void RefreshState()
    {
        IsRunning = _service.IsRunning;
        RegisteredListCount = _service.Registry.Count;
        EnabledListCount = _service.Registry.EnabledCount;

        StatusText = _service.IsRunning
            ? $"Scheduler activo. {RegisteredListCount} lista(s), {EnabledListCount} habilitada(s)."
            : "Scheduler detenido.";

        OnPropertyChanged(nameof(RegisteredText));
        OnPropertyChanged(nameof(EnabledText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateColor));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateColor));
    }

    partial void OnPlaybackStateChanged(PlaybackStateInfo value)
    {
        OnPropertyChanged(nameof(IsSchedulerActive));
        OnPropertyChanged(nameof(ShowPlaylistPanel));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnPlaylistStateChanged(PlaylistPlayerState value)
    {
        OnPropertyChanged(nameof(IsPlaylistActive));
        OnPropertyChanged(nameof(IsPlaylistSuspended));
        OnPropertyChanged(nameof(ShowPlaylistPanel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(PlaylistHeaderText));
        OnPropertyChanged(nameof(PlaylistTrackText));
        OnPropertyChanged(nameof(PlaylistProgressText));
        OnPropertyChanged(nameof(PlaylistPositionText));
        OnPropertyChanged(nameof(PlaylistProgressFraction));
        OnPropertyChanged(nameof(PlaylistStatusText));
        OnPropertyChanged(nameof(PlaylistStatusColorKey));
    }
}

/// -------------
/// Representa un item programado que esta en cola esperando su turno.
/// -------------
public sealed class QueuedItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _title = string.Empty;
    private string _listName = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string ListName
    {
        get => _listName;
        set => SetProperty(ref _listName, value);
    }

    public RadioMaker.Core.Scheduling.SchedulePriority Priority { get; init; }

    /// -------------True si el item es una tanda (multiples pistas).</summary>
    public bool IsTanda { get; init; }

    /// -------------Cantidad de pistas si es una tanda.</summary>
    public int TandaTrackCount { get; init; }

    /// -------------True si este item representa a la playlist que fue pausada.</summary>
    public bool IsSuspendedPlaylist { get; init; }

    public string PriorityText => IsSuspendedPlaylist
        ? "PLAYLIST"
        : Priority == RadioMaker.Core.Scheduling.SchedulePriority.High
            ? "ALTA"
            : "Baja";

    public string PriorityColor => IsSuspendedPlaylist
        ? "Accent"
        : Priority == RadioMaker.Core.Scheduling.SchedulePriority.High
            ? "Warning"
            : "FgMuted";

    public string SourceText
    {
        get
        {
            if (IsSuspendedPlaylist)
                return string.IsNullOrEmpty(ListName) ? "se reanudara" : $"pista: {ListName}";

            if (IsTanda && TandaTrackCount > 0)
                return $"tanda, {TandaTrackCount} pista/s";

            return string.IsNullOrEmpty(ListName) ? string.Empty : $"desde {ListName}";
        }
    }
}