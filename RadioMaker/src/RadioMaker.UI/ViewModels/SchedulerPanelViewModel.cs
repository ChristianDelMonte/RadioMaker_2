using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Localization;

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

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
    private readonly List<SchedulerLogEntry> _sourceLogEntries = new();

    private const int MaxVisibleLogEntries = 200;

    // ───── Estado del scheduler ──────────────────────────────────────────

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = LocalizationService.GetString("Loc.Scheduler.Status.Stopped");
    [ObservableProperty] private int _registeredListCount;
    [ObservableProperty] private int _enabledListCount;

    public ObservableCollection<SchedulerLogEntry> LogEntries { get; } = new();

    /// -------------
    /// Items programados que estan en cola esperando su turno de
    /// reproduccion (porque otro item esta sonando). Se ordenan por
    /// prioridad (ALTA primero) y luego por orden de llegada.
    /// -------------
    public ObservableCollection<QueuedItemViewModel> QueuedItems { get; } = new();
    public string QueuedItemsCountText => string.Format(LocalizationService.GetString("Loc.Controls.Scheduler.QueueCount"), QueuedItems.Count);

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

    public string RegisteredText => string.Format(LocalizationService.GetString("Loc.Scheduler.Registered"), RegisteredListCount);
    public string EnabledText => string.Format(LocalizationService.GetString("Loc.Scheduler.Enabled"), EnabledListCount);
    public string StateText => LocalizationService.GetString(IsRunning ? "Loc.Scheduler.State.Active" : "Loc.Scheduler.State.Stopped");
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
            ? LocalizationService.GetString("Loc.Scheduler.Playlist")
            : PlaylistState.PlaylistName;

    public string PlaylistTrackText => PlaylistState.TrackTitle;
    public string PlaylistProgressText => PlaylistState.TrackIndex >= 0
        ? string.Format(LocalizationService.GetString("Loc.Scheduler.TrackProgress"), PlaylistState.TrackIndex + 1, PlaylistState.TrackCount)
        : string.Empty;
    public string PlaylistPositionText => PlaylistState.PositionText;
    public double PlaylistProgressFraction => PlaylistState.ProgressFraction;
    public string PlaylistStatusText => PlaylistState.Status switch
    {
        PlaylistPlayerStatus.Stopped => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.Stopped"),
        PlaylistPlayerStatus.Playing => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.Playing"),
        PlaylistPlayerStatus.Paused => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.Paused"),
        PlaylistPlayerStatus.PausedByScheduler => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.PausedByScheduler"),
        PlaylistPlayerStatus.Delaying => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.Waiting"),
        PlaylistPlayerStatus.Error => LocalizationService.GetString("Loc.Scheduler.PlaylistStatus.Error"),
        _ => "?"
    };
    public string PlaylistStatusColorKey => PlaylistState.StatusColor;

    // ───── CONSTRUCTOR ───────────────────────────────────────────────────
    private readonly Avalonia.Threading.DispatcherTimer _queueRefreshTimer;
    private readonly EventHandler _queueRefreshHandler;
    public SchedulerPanelViewModel(SchedulerService service, PlaybackEngine? playback = null, PlaylistPlayer? playlistPlayer = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _playback = playback;
        _playlistPlayer = playlistPlayer;
        LocalizationService.LanguageChanged += OnLanguageChanged;

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
        _queueRefreshHandler = (_, _) => RefreshQueue();
        _queueRefreshTimer.Tick += _queueRefreshHandler;
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
            OnPropertyChanged(nameof(QueuedItemsCountText));
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
            _sourceLogEntries.Insert(0, entry);
            LogEntries.Insert(0, LocalizeLogEntry(entry));

            while (LogEntries.Count > MaxVisibleLogEntries)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
                _sourceLogEntries.RemoveAt(_sourceLogEntries.Count - 1);
            }

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
            StatusText = string.Format(LocalizationService.GetString("Loc.Scheduler.Status.StartError"), ex.Message);
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
            StatusText = string.Format(LocalizationService.GetString("Loc.Scheduler.Status.StopError"), ex.Message);
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
            StatusText = LocalizationService.GetString("Loc.Scheduler.Status.Reset");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationService.GetString("Loc.Scheduler.Status.Error"), ex.Message);
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
            StatusText = string.Format(LocalizationService.GetString("Loc.Scheduler.Status.Error"), ex.Message);
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
            StatusText = string.Format(LocalizationService.GetString("Loc.Scheduler.Status.StopPlaybackError"), ex.Message);
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void ReloadLog()
    {
        LogEntries.Clear();
        _sourceLogEntries.Clear();

        var snapshot = _service.GetLogSnapshot();
        foreach (var entry in snapshot.Take(MaxVisibleLogEntries))
        {
            _sourceLogEntries.Add(entry);
            LogEntries.Add(LocalizeLogEntry(entry));
        }

        OnPropertyChanged(nameof(HasLog));
        OnPropertyChanged(nameof(HasNoLog));
    }

    private void RefreshState()
    {
        IsRunning = _service.IsRunning;
        RegisteredListCount = _service.Registry.Count;
        EnabledListCount = _service.Registry.EnabledCount;

        StatusText = _service.IsRunning
            ? string.Format(LocalizationService.GetString("Loc.Scheduler.Status.Running"), RegisteredListCount, EnabledListCount)
            : LocalizationService.GetString("Loc.Scheduler.Status.Stopped");

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

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshState();
        OnPropertyChanged(nameof(QueuedItemsCountText));
        OnPropertyChanged(nameof(PlaylistHeaderText));
        OnPropertyChanged(nameof(PlaylistProgressText));
        OnPropertyChanged(nameof(PlaylistStatusText));
        foreach (var item in QueuedItems) item.RefreshLanguage();
        for (int i = 0; i < LogEntries.Count && i < _sourceLogEntries.Count; i++)
            LogEntries[i] = LocalizeLogEntry(_sourceLogEntries[i]);
    }

    private static SchedulerLogEntry LocalizeLogEntry(SchedulerLogEntry entry) =>
        entry with { Message = LocalizeLogMessage(entry.Message) };

    internal static string LocalizeLogMessage(string message)
    {
        string? Match(string pattern, string key)
        {
            var match = Regex.Match(message, pattern);
            if (!match.Success) return null;
            var values = match.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value).ToArray();
            return string.Format(LocalizationService.GetString(key), values);
        }

        string? value = Match(@"^Disparando: (.+) \((.+)\) desde '(.+)'$", "Loc.Scheduler.Log.Trigger");
        if (value is not null) return value;
        value = Match(@"^Error en tick: (.*)$", "Loc.Scheduler.Log.TickError");
        if (value is not null) return value;
        value = Match(@"^Error en handler de ItemTriggered: (.*)$", "Loc.Scheduler.Log.TriggerHandlerError");
        if (value is not null) return value;
        value = Match(@"^Pausa solicitada: (.+)\.$", "Loc.Scheduler.Log.Pause");
        if (value is not null) return value.Replace("reproducción programada", LocalizationService.GetString("Loc.Scheduler.Log.ScheduledPlayback"), StringComparison.Ordinal);
        value = Match(@"^Reanudación solicitada: (.+)\.$", "Loc.Scheduler.Log.Resume");
        if (value is not null) return value.Replace("reproducción programada", LocalizationService.GetString("Loc.Scheduler.Log.ScheduledPlayback"), StringComparison.Ordinal);
        value = Match(@"^Siguiente solicitado durante: (.+)\.$", "Loc.Scheduler.Log.Next");
        if (value is not null) return value.Replace("reproducción programada", LocalizationService.GetString("Loc.Scheduler.Log.ScheduledPlayback"), StringComparison.Ordinal);
        value = Match(@"^Anterior solicitado durante: (.+)\.$", "Loc.Scheduler.Log.Previous");
        if (value is not null) return value.Replace("reproducción programada", LocalizationService.GetString("Loc.Scheduler.Log.ScheduledPlayback"), StringComparison.Ordinal);
        value = Match(@"^Play manual: '(.+)' -> (\d+) item\(s\) encolados \(1 sonando \+ (\d+) en cola\)\.$", "Loc.Scheduler.Log.ManualQueued");
        if (value is not null) return value;
        value = Match(@"^'(.+)' ya esta en cola, se ignora el disparo duplicado\.$", "Loc.Scheduler.Log.Duplicate");
        if (value is not null) return value;
        value = Match(@"^Cola llena \((\d+)\), se descarta '(.+)'\.$", "Loc.Scheduler.Log.QueueFull");
        if (value is not null) return value;
        value = Match(@"^'(.+)' en cola \((.+)\)\. Total: (\d+)\.$", "Loc.Scheduler.Log.Enqueued");
        if (value is not null)
        {
            value = value.Replace("otro item ALTA sonando", LocalizationService.GetString("Loc.Scheduler.QueueReason.OtherHigh"), StringComparison.Ordinal)
                .Replace("item ALTA sonando", LocalizationService.GetString("Loc.Scheduler.QueueReason.High"), StringComparison.Ordinal)
                .Replace("otro item BAJA en curso", LocalizationService.GetString("Loc.Scheduler.QueueReason.OtherLow"), StringComparison.Ordinal);
            return value;
        }
        value = Match(@"^Sacando de cola: '(.+)' \(quedan (\d+)\)\.$", "Loc.Scheduler.Log.Dequeued");
        if (value is not null) return value;
        value = Match(@"^Limpiando cola \((\d+) items pendientes\)\.$", "Loc.Scheduler.Log.QueueCleared");
        if (value is not null) return value;
        value = Match(@"^Error cargando tanda '(.+)': (.*)$", "Loc.Scheduler.Log.LoadSequenceError");
        if (value is not null) return value;
        value = Match(@"^'(.+)': usando tanda embebida \(formato antiguo\)\. Recomendado: volver a guardarla como \.tnd y referenciarla\.$", "Loc.Scheduler.Log.LegacySequence");
        if (value is not null) return value;
        value = Match(@"^'(.+)' no tiene una tanda valida \(archivo no encontrado o vacia\)\.$", "Loc.Scheduler.Log.InvalidSequence");
        if (value is not null) return value;
        value = Match(@"^'(.+)' no tiene pistas habilitadas con archivo\.$", "Loc.Scheduler.Log.NoEnabledTracks");
        if (value is not null) return value;
        value = Match(@"^Reproduciendo Tanda '(.+)' \((\d+) pistas\) desde '(.+)' \((ALTA|Baja)\)\.$", "Loc.Scheduler.Log.PlayingSequence");
        if (value is not null) return value.Replace("(ALTA)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.High") + ")", StringComparison.Ordinal).Replace("(Baja)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.Low") + ")", StringComparison.Ordinal);
        value = Match(@"^'(.+)' no tiene playlist asignada\.$", "Loc.Scheduler.Log.NoPlaylist");
        if (value is not null) return value;
        value = Match(@"^Archivo de playlist no encontrado para '(.+)': (.+)$", "Loc.Scheduler.Log.PlaylistFileMissing");
        if (value is not null) return value;
        value = Match(@"^Error cargando playlist '(.+)': (.*)$", "Loc.Scheduler.Log.LoadPlaylistError");
        if (value is not null) return value;
        value = Match(@"^'(.+)' tiene una playlist vacia o no se pudo cargar\.$", "Loc.Scheduler.Log.EmptyPlaylist");
        if (value is not null) return value;
        value = Match(@"^'(.+)' no tiene pistas habilitadas reproducibles\.$", "Loc.Scheduler.Log.NoPlayableTracks");
        if (value is not null) return value;
        value = Match(@"^Reproduciendo Playlist '(.+)' \((\d+) pistas\) desde '(.+)' \((ALTA|Baja)\)\.$", "Loc.Scheduler.Log.PlayingPlaylist");
        if (value is not null) return value.Replace("(ALTA)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.High") + ")", StringComparison.Ordinal).Replace("(Baja)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.Low") + ")", StringComparison.Ordinal);
        value = Match(@"^'(.+)' es un bloque vacio\.$", "Loc.Scheduler.Log.EmptyBlock");
        if (value is not null) return value;
        value = Match(@"^'(.+)' no tiene contenido reproducible \(todos los items faltan o estan vacios\)\.$", "Loc.Scheduler.Log.UnplayableBlock");
        if (value is not null) return value;
        value = Match(@"^Reproduciendo Bloque '(.+)' \((\d+) pista\(s\) totales desde (\d+) item\(s\)\) desde '(.+)' \((ALTA|Baja)\)\.$", "Loc.Scheduler.Log.PlayingBlock");
        if (value is not null) return value.Replace("(ALTA)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.High") + ")", StringComparison.Ordinal).Replace("(Baja)", "(" + LocalizationService.GetString("Loc.Scheduler.Queue.Low") + ")", StringComparison.Ordinal);
        value = Match(@"^Bloque '(.+)': archivo no encontrado '(.+)'\. Se saltea\.$", "Loc.Scheduler.Log.BlockAudioMissing");
        if (value is not null) return value;
        value = Match(@"^Bloque '(.+)': playlist no encontrada '(.+)'\. Se saltea\.$", "Loc.Scheduler.Log.BlockPlaylistMissing");
        if (value is not null) return value;
        value = Match(@"^Bloque '(.+)': playlist vacia o ilegible '(.+)'\. Se saltea\.$", "Loc.Scheduler.Log.BlockPlaylistUnreadable");
        if (value is not null) return value;
        value = Match(@"^Bloque '(.+)': tanda no encontrada '(.+)'\. Se saltea\.$", "Loc.Scheduler.Log.BlockSequenceMissing");
        if (value is not null) return value;
        value = Match(@"^Bloque '(.+)': tanda vacia o ilegible '(.+)'\. Se saltea\.$", "Loc.Scheduler.Log.BlockSequenceUnreadable");
        if (value is not null) return value;
        value = Match(@"^Error reproduciendo '(.+)': (.*)$", "Loc.Scheduler.Log.PlaybackError");
        if (value is not null) return value;
        value = Match(@"^Pista (\d+) de '(.+)' no tiene archivo valido\. Se saltea\.$", "Loc.Scheduler.Log.SequenceTrackMissing");
        if (value is not null) return value;
        value = Match(@"^'(.+)': pista (\d+)/(\d+) — '(.+)'\.$", "Loc.Scheduler.Log.SequenceTrackPlaying");
        if (value is not null) return value;
        value = Match(@"^Error reproduciendo pista (\d+) de '(.+)': (.*)$", "Loc.Scheduler.Log.SequenceTrackError");
        if (value is not null) return value;
        value = Match(@"^Esperando ([\d.,]+)s antes de la pista (\d+)\.\.\.$", "Loc.Scheduler.Log.DelayBeforeTrack");
        if (value is not null) return value;
        value = Match(@"^Termino '(.+)'\.$", "Loc.Scheduler.Log.ItemEnded");
        if (value is not null) return value;
        value = Match(@"^'(.+)': pista (\d+)/(\d+) — LOCUCIÓN: (.+)\.$", "Loc.Scheduler.Log.VoiceTrackPlaying");
        if (value is not null) return value;
        value = Match(@"^Suspendido '(.+)'(.*) en ([\d.,]+)s \(sera reanudado\)\.$", "Loc.Scheduler.Log.Suspended");
        if (value is not null) return value;
        value = Match(@"^Error suspendiendo canal actual: (.*)$", "Loc.Scheduler.Log.SuspendError");
        if (value is not null) return value;
        value = Match(@"^No se pudo reanudar '(.+)' \(tanda no disponible\)\.$", "Loc.Scheduler.Log.ResumeSequenceFailed");
        if (value is not null) return value;
        value = Match(@"^Reanudando '(.+)' desde pista (\d+) en ([\d.,]+)s\.$", "Loc.Scheduler.Log.ResumeSequence");
        if (value is not null) return value;
        value = Match(@"^Reanudando '(.+)' desde ([\d.,]+)s\.$", "Loc.Scheduler.Log.ResumeItem");
        if (value is not null) return value;

        return message switch
        {
            "Scheduler iniciado." => LocalizationService.GetString("Loc.Scheduler.Log.Started"),
            "Scheduler detenido." => LocalizationService.GetString("Loc.Scheduler.Log.Stopped"),
            "Pausa solicitada durante una locución de la tanda." => LocalizationService.GetString("Loc.Scheduler.Log.PauseDuringVoice"),
            "Detener todo solicitado para el Scheduler." => LocalizationService.GetString("Loc.Scheduler.Log.StopAllRequested"),
            "Play manual: la lista esta vacia." => LocalizationService.GetString("Loc.Scheduler.Log.ManualListEmpty"),
            "Play manual: no hay items habilitados para reproducir." => LocalizationService.GetString("Loc.Scheduler.Log.ManualNoEnabledItems"),
            "Estado de disparos reiniciado." => LocalizationService.GetString("Loc.Scheduler.Status.Reset"),
            "Reproduccion programada detenida manualmente." => LocalizationService.GetString("Loc.Scheduler.Log.ManualStopRequested"),
            _ => message
        };
    }

    public void Dispose()
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _service.LogAdded -= OnLogAdded;
        _service.StateChanged -= OnServiceStateChanged;
        if (_playback is not null) _playback.StateChanged -= OnPlaybackStateChanged;
        if (_playlistPlayer is not null) _playlistPlayer.StateChanged -= OnPlaylistStateChanged;
        _queueRefreshTimer.Stop();
        _queueRefreshTimer.Tick -= _queueRefreshHandler;
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
        ? LocalizationService.GetString("Loc.Scheduler.Queue.Playlist")
        : Priority == RadioMaker.Core.Scheduling.SchedulePriority.High
            ? LocalizationService.GetString("Loc.Scheduler.Queue.High")
            : LocalizationService.GetString("Loc.Scheduler.Queue.Low");

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
                return string.IsNullOrEmpty(ListName)
                    ? LocalizationService.GetString("Loc.Scheduler.Queue.WillResume")
                    : string.Format(LocalizationService.GetString("Loc.Scheduler.Queue.Track"), ListName);

            if (IsTanda && TandaTrackCount > 0)
                return string.Format(LocalizationService.GetString("Loc.Scheduler.Queue.Tanda"), TandaTrackCount);

            return string.IsNullOrEmpty(ListName)
                ? string.Empty
                : string.Format(LocalizationService.GetString("Loc.Scheduler.Queue.From"), ListName);
        }
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(SourceText));
    }
}
