using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Playback;
using System.Threading.Channels;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Motor de reproduccion programada. Soporta items SingleFile, Tanda,
/// Playlist y Block.
///
/// Reglas de prioridad:
///   - Alta sobre Baja: suspende lo que esta sonando (SingleFile, Tanda,
///     Playlist o Block) y arranca el Alta. Al terminar el Alta, se reanuda
///     lo suspendido desde la posicion y pista exactas.
///   - Alta sobre Alta: se encola.
///   - Baja sobre Alta: se encola.
///   - Baja sobre Baja: se encola.
///
/// Sobre las Playlists:
///   Un item de tipo Playlist apunta a un archivo .rmf en disco. Al dispararse,
///   el engine carga el .rmf, lo convierte a una Tanda virtual en memoria y
///   reusa TODO el flujo de tanda (cola, delays, mixados, resume, etc).
///
/// Sobre los Bloques:
///   Un item de tipo Block contiene una lista ordenada de referencias
///   (archivos, playlists, tandas). Al dispararse, el engine APLANA el bloque
///   a una tanda virtual concatenando todo el contenido de sus items en orden
///   y reusa el mismo flujo de tanda. Los horarios de los items internos se
///   ignoran: solo cuenta el horario del bloque.
///
/// Mixados intermedios:
///   Si una pista tiene un MixAudioPath asignado, el engine programa su
///   disparo con un timer y superpone el audio del mix, bajando el nivel de
///   la pista principal mientras dura.
///
/// Procesamiento automatico:
///   El engine expone un hook ConfigureChannel que la UI puede setear.
///   Antes de cada Play, se llama al hook para que aplique EQ, Trim y
///   Normalizacion segun las settings del usuario.
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly SchedulerService _scheduler;
    private readonly SchedulerState _schedulerState;
    private readonly Action<Action> _dispatch;
    private readonly BusExclusivityCoordinator? _coordinator;
    private readonly LocutionService? _locution;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private string _acquiredBusId = string.Empty;

    private readonly object _gate = new();

    private IAudioBus? _targetBus;
    private bool _autoFadeEnabled;
    private double _autoFadeSeconds = 2.0;

    /// <summary>
    /// Hook opcional llamado antes de cada Play.
    /// Parametros: (canal, ruta del archivo, loguear detalles).
    /// Cuando logDetails es false, solo aplica la configuracion sin escribir
    /// en el log (evita duplicar entradas al reaplicar).
    /// </summary>
    public Action<IAudioChannel, string, bool>? ConfigureChannel { get; set; }

    /// <summary>
    /// Hook opcional para pre-calentar el analisis de la proxima pista de
    /// una tanda/playlist/bloque. Igual que el de PlaylistPlayer.
    /// </summary>
    public Action<string>? PrewarmFile { get; set; }

    /// <summary>
    /// Hook opcional llamado cuando la UI termina un analisis en background.
    /// La UI comprueba si el archivo analizado es el que esta sonando y
    /// reaplica los ajustes en vivo.
    /// </summary>
    public Func<string>? CurrentFilePathProvider { get; set; }

    // ───── Estado de reproduccion actual ─────────────────────────────────

    private ScheduledItem? _currentItem;
    private IAudioChannel? _currentChannel;
    private string _currentFilePath = string.Empty;
    private SchedulePriority _currentPriority = SchedulePriority.Low;
    private string _currentListName = string.Empty;

    // ───── Estado de mixado intermedio ───────────────────────────────────

    private IAudioChannel? _mixChannel;
    private System.Threading.Timer? _mixTimer;
    private double _mixOriginalNormGainDb;
    private CancellationTokenSource? _mixLocutionCts;

    // ───── Estado de tanda/playlist/bloque en curso (unificado) ──────────

    private Tanda? _currentTanda;
    private List<TandaTrack> _tandaQueue = new();
    private int _tandaIndex = 0;
    private CancellationTokenSource? _tandaLocutionCts;
    private bool _tandaLocutionPaused;

    private System.Threading.Timer? _tandaDelayTimer;

    // ───── Estado de lo suspendido ───────────────────────────────────────

    private SuspendedState? _suspended;

    // ───── Timer de progreso ─────────────────────────────────────────────

    private System.Threading.Timer? _progressTimer;

    private sealed class SuspendedState
    {
        public ScheduledItem Item { get; init; } = new();
        public string ListName { get; init; } = string.Empty;
        public int TandaIndex { get; init; } = -1;
        public List<TandaTrack> TandaQueue { get; init; } = new();
        public double PositionSeconds { get; init; }
        public double DurationSeconds { get; init; }

        /// <summary>
        /// Instancia activa de tanda conservada al suspender. Puede ser
        /// virtual (Playlist/Block), cargada desde .tnd o embebida.
        /// </summary>
        public Tanda? VirtualTanda { get; init; }
    }

    private sealed class PendingItem
    {
        public ScheduledItem Item { get; init; } = new();
        public string ListName { get; init; } = string.Empty;
        public DateTime EnqueuedAt { get; init; } = DateTime.Now;
    }

    private readonly List<PendingItem> _pendingQueue = new();
    private const int MaxQueueSize = 500;
    /// <summary>
    /// True mientras hay un Play manual (iniciado con PlayList()) en curso.
    /// Se limpia SOLO cuando la cola manual termina o cuando se detiene.
    /// </summary>
    private bool _isManualPlayInProgress;

    // ───── Eventos ───────────────────────────────────────────────────────

    public event EventHandler<PlaybackStateInfo>? StateChanged;
    /// <summary>
    /// Se dispara cuando un Play manual (iniciado con PlayList) termina
    /// naturalmente o es detenido. NO se dispara por disparos del scheduler.
    /// </summary>
    public event EventHandler? ManualPlayFinished;

    /// <summary>
    /// True si el Play manual iniciado con PlayList() sigue en curso
    /// (incluye estados de suspension por un programado ALTA).
    /// </summary>
    public bool IsManualPlayInProgress => _isManualPlayInProgress;

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaybackEngine(
        IAudioEngine audioEngine,
        SchedulerService scheduler,
        Action<Action> dispatch,
        BusExclusivityCoordinator? coordinator = null,
        LocutionService? locution = null)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _schedulerState = scheduler.State;
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _coordinator = coordinator;
        _locution = locution;

        if (_coordinator is not null)
            _coordinator.ChannelExpropriated += OnChannelExpropriated;

        _scheduler.ItemTriggered += OnItemTriggered;
    }

    // ───── Configuracion publica ─────────────────────────────────────────

    public void SetBus(IAudioBus? bus)
    {
        lock (_gate) _targetBus = bus;
    }

    public void SetAutoFade(bool enabled, double seconds)
    {
        lock (_gate)
        {
            _autoFadeEnabled = enabled;
            _autoFadeSeconds = Math.Clamp(seconds, 0.1, 30.0);
        }
    }

    public IAudioChannel? CurrentChannel => _currentChannel;
    public string CurrentFilePath => _currentFilePath;

    /// <summary>
    /// ID del item que esta sonando AHORA en el motor (o null si no hay).
    /// Se usa para que la UI identifique qué pestaña de Programados esta
    /// al aire.
    /// </summary>
    public string? CurrentItemId
    {
        get
        {
            lock (_gate) return _currentItem?.Id;
        }
    }

    /// <summary>
    /// ID del item suspendido (pausado por un ALTA, o por coexistencia).
    /// Null si no hay suspendido.
    /// </summary>
    public string? SuspendedItemId
    {
        get
        {
            lock (_gate) return _suspended?.Item.Id;
        }
    }

    public IReadOnlyList<QueuedItemInfo> GetQueuedItems()
    {
        lock (_gate)
        {
            return _pendingQueue
                .OrderByDescending(p => p.Item.Priority)
                .ThenBy(p => p.EnqueuedAt)
                .Select(p => new QueuedItemInfo(
                    Title: p.Item.DisplayTitle,
                    ListName: p.ListName,
                    Priority: p.Item.Priority,
                    IsTanda: p.Item.Kind == ScheduledItemKind.Tanda
                             || p.Item.Kind == ScheduledItemKind.Playlist
                             || p.Item.Kind == ScheduledItemKind.Block,
                    TandaTrackCount: p.Item.EmbeddedTanda?.Tracks.Count ?? 0))
                .ToList();
        }
    }

    public string? GetSuspendedItemTitle()
    {
        lock (_gate)
        {
            return _suspended?.Item.DisplayTitle;
        }
    }

    public (int Index, int Count, string TrackTitle)? GetSuspendedTandaInfo()
    {
        lock (_gate)
        {
            if (_suspended is null) return null;
            if (_suspended.TandaIndex < 0) return null;
            if (_suspended.TandaQueue.Count == 0) return null;

            int idx = _suspended.TandaIndex;
            if (idx >= _suspended.TandaQueue.Count) return null;

            return (idx, _suspended.TandaQueue.Count, _suspended.TandaQueue[idx].DisplayTitle);
        }
    }

    public (int Index, int Count, string Title, string FilePath)? GetNextTandaTrackInfo()
    {
        lock (_gate)
        {
            if (_currentTanda is null) return null;
            if (_currentItem is null) return null;
            if (_tandaQueue.Count == 0) return null;

            int nextIndex = _tandaIndex + 1;
            if (nextIndex < 0 || nextIndex >= _tandaQueue.Count)
                return null;

            var track = _tandaQueue[nextIndex];
            if (track is null) return null;

            return (nextIndex, _tandaQueue.Count, track.DisplayTitle, track.FilePath);
        }
    }

    public void ReapplyCurrentChannelConfig()
    {
        var ch = _currentChannel;
        var path = _currentFilePath;

        if (ch is null) return;
        if (string.IsNullOrWhiteSpace(path)) return;

        try { ConfigureChannel?.Invoke(ch, path, false); } catch { }
    }

    public IEnumerable<IAudioChannel> GetActiveChannels()
    {
        var list = new List<IAudioChannel>();

        lock (_gate)
        {
            if (_currentChannel is not null)
                list.Add(_currentChannel);

            if (_mixChannel is not null)
                list.Add(_mixChannel);
        }

        return list;
    }

    // =====================================================================
    // TRANSPORTE: Pause / Resume / Next
    // =====================================================================

    /// <summary>
    /// Pausa el item que está sonando ahora (SingleFile, Tanda, Playlist o Block).
    /// No afecta la cola pendiente ni lo suspendido. No-op si no hay nada sonando
    /// o si ya está pausado.
    /// </summary>
    public void Pause()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_tandaLocutionCts is not null)
                {
                    _tandaLocutionPaused = true;
                    CancelTandaLocution();
                    return;
                }
                if (_currentChannel is null) return;
                try
                {
                    if (_currentChannel.State != PlaybackState.Playing) return;
                    _currentChannel.Pause();
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Reanuda el item que fue pausado con <see cref="Pause"/>.
    /// No-op si no hay nada pausado.
    /// </summary>
    public void Resume()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_tandaLocutionPaused && _currentTanda is not null &&
                    _tandaIndex >= 0 && _tandaIndex < _tandaQueue.Count)
                {
                    _tandaLocutionPaused = false;
                    PlayLocutionInTanda(_tandaIndex, _tandaQueue[_tandaIndex]);
                    return;
                }
                if (_currentChannel is null) return;
                try
                {
                    if (_currentChannel.State != PlaybackState.Paused) return;
                    _currentChannel.Play();
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Salta al siguiente item. Si el item actual es una tanda, salta al siguiente
    /// track de la tanda. Si era el último track, o si era un SingleFile, pasa al
    /// siguiente item de la cola (o reanuda el suspendido, o termina).
    ///
    /// Es equivalente a "fin natural del item actual", pero sin esperar el
    /// transcurso del audio.
    /// </summary>
    public void Next()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_currentChannel is null && _currentTanda is null) return;

                CancelTandaLocution();
                _tandaLocutionPaused = false;

                StopProgressTimer();
                StopTandaDelayTimer();
                StopMixTimer();
                StopMixChannel();
                _mixOriginalNormGainDb = 0;

                try
                {
                    if (_currentChannel is not null)
                    {
                        _currentChannel.Ended -= OnChannelEnded;
                        _currentChannel.Stop();
                        _currentChannel.Dispose();
                    }
                }
                catch { }
                _currentChannel = null;
                _currentFilePath = string.Empty;

                // Si estamos dentro de una tanda/playlist/bloque y hay más tracks,
                // avanzar al siguiente track.
                if (_currentTanda is not null && _tandaIndex + 1 < _tandaQueue.Count)
                {
                    PlayTandaTrack(_tandaIndex + 1, 0);
                    return;
                }

                // Item terminó (o era SingleFile): procesar cola / suspendido / fin.
                var endedId = _currentItem?.Id;
                _currentItem = null;
                _currentTanda = null;
                _tandaQueue.Clear();
                _tandaIndex = 0;
                _currentPriority = SchedulePriority.Low;
                _currentListName = string.Empty;

                if (!string.IsNullOrEmpty(endedId))
                    _schedulerState.UnmarkPlaying(endedId);

                if (_pendingQueue.Count > 0)
                {
                    ProcessQueue();
                    if (_currentItem is null)
                    {
                        if (_suspended is not null) ResumeSuspended();
                        else FinishPlayback();
                    }
                }
                else if (_suspended is not null)
                {
                    ResumeSuspended();
                }
                else
                {
                    FinishPlayback();
                }
            }
        });
    }

    /// <summary>
    /// Retrocede al track anterior si estamos dentro de una tanda/playlist/bloque.
    /// Si ya estamos en el primer track (o es un SingleFile), reinicia el item
    /// actual desde el segundo 0 (comportamiento estándar de reproductores).
    /// </summary>
    public void Previous()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_currentChannel is null) return;

                // Caso 1: estamos dentro de una tanda y hay un track anterior.
                if (_currentTanda is not null && _tandaIndex > 0)
                {
                    StopProgressTimer();
                    StopTandaDelayTimer();
                    StopMixTimer();
                    StopMixChannel();
                    _mixOriginalNormGainDb = 0;

                    try
                    {
                        _currentChannel.Ended -= OnChannelEnded;
                        _currentChannel.Stop();
                        _currentChannel.Dispose();
                    }
                    catch { }
                    _currentChannel = null;
                    _currentFilePath = string.Empty;

                    PlayTandaTrack(_tandaIndex - 1, 0);
                    return;
                }

                // Caso 2: primer track de una tanda, o SingleFile → seek al inicio.
                try
                {
                    _currentChannel.Seek(TimeSpan.Zero);
                }
                catch { }
            }
        });
    }

    public void StopAll()
    {
        _dispatch(() =>
        {
            StopCurrentPlaybackInternal(unmarkScheduler: true);
            _suspended = null;
            ClearQueue();

            // Si habia un Play manual en curso, terminarlo y notificar.
            if (_isManualPlayInProgress)
            {
                _isManualPlayInProgress = false;

                try { ManualPlayFinished?.Invoke(this, EventArgs.Empty); }
                catch { }
            }

            RaiseState(PlaybackStateInfo.Idle);
        });
    }

    /// <summary>
    /// Inicia la reproduccion MANUAL de una lista de items programados.
    ///
    /// A diferencia del disparo por scheduler:
    ///   - NO espera horarios: reproduce todo en el orden dado, ya mismo.
    ///   - Detiene lo que este sonando en este momento y limpia la cola.
    ///   - Ignora la prioridad de cada item para respetar el orden EXACTO
    ///     de la lista (los items se encolan con prioridad normalizada).
    ///   - Los items deshabilitados se saltean.
    ///
    /// Si durante la reproduccion el scheduler dispara un item ALTA,
    /// el Play manual se suspende y se reanuda al terminar (esto ya lo
    /// maneja el engine con la logica de prioridad/suspension).
    ///
    /// Llamar desde el hilo de UI: internamente usa el dispatcher.
    /// </summary>
    /// <returns>Cantidad de items que se encolaron (0 si nada).</returns>
    public int PlayList(IReadOnlyList<ScheduledItem> items, string listName)
    {
        if (items is null || items.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                "Play manual: la lista esta vacia.");
            return 0;
        }

        // Filtrar items habilitados, en el orden original.
        var queue = items.Where(i => i is not null && i.IsEnabled).ToList();
        if (queue.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                "Play manual: no hay items habilitados para reproducir.");
            return 0;
        }

        // IMPORTANTE: calcular el count ANTES del dispatch. El bloque del
        // dispatch se encola y se ejecuta despues de que este metodo retorna,
        // asi que si calcularamos el count adentro, el caller recibiria 0.
        int enqueued = queue.Count;

        _dispatch(() =>
        {
            // 1) Cortar todo lo que este sonando.
            StopCurrentPlaybackInternal(unmarkScheduler: true);
            _suspended = null;
            ClearQueue();

            // Marcar que arranca un Play manual. Se limpia cuando la cola
            // se vacia (final natural) o cuando el usuario detiene.
            _isManualPlayInProgress = true;

            // 2) Arrancar el primero.
            var first = queue[0];
            StartItem(first, listName);

            // 3) Encolar el resto con prioridad normalizada.
            //    Forzamos Low para que NO se reordenen por prioridad:
            //    el usuario quiere que su orden se respete tal cual.
            for (int i = 1; i < queue.Count; i++)
            {
                var item = queue[i];

                // Clonamos el item con prioridad Low para el disparo manual.
                // Clonamos para no modificar el original persistido.
                var normalized = new ScheduledItem
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Priority = SchedulePriority.Low,
                    Entries = item.Entries,
                    IsEnabled = item.IsEnabled,
                    Notes = item.Notes,
                    DurationSeconds = item.DurationSeconds,
                    EmbeddedTanda = item.EmbeddedTanda,
                    EmbeddedBlock = item.EmbeddedBlock
                };

                _pendingQueue.Add(new PendingItem
                {
                    Item = normalized,
                    ListName = listName,
                    EnqueuedAt = DateTime.Now
                });
            }

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Play manual: '{listName}' -> {enqueued} item(s) encolados (1 sonando + {enqueued - 1} en cola).");
        });

        return enqueued;
    }

    // ───── Handler de disparos ───────────────────────────────────────────

    private void OnItemTriggered(object? sender, SchedulerEventArgs e)
    {
        var item = e.Item;
        var listName = e.SourceListName;

        if (item is null) return;

        _dispatch(() => HandleTrigger(item, listName));
    }

    private void HandleTrigger(ScheduledItem item, string listName)
    {
        lock (_gate)
        {
            if (_currentItem is not null && _currentItem.Id == item.Id) return;
            if (_suspended is not null && _suspended.Item.Id == item.Id) return;

            if (_pendingQueue.Any(p => p.Item.Id == item.Id))
            {
                _scheduler.AddLog(SchedulerLogLevel.Info,
                    $"'{item.DisplayTitle}' ya esta en cola, se ignora el disparo duplicado.");
                return;
            }

            var incoming = item.Priority;

            if (incoming == SchedulePriority.High && _currentPriority == SchedulePriority.Low && _currentItem is not null)
            {
                SuspendCurrent();
                StartItem(item, listName);
                return;
            }

            if (incoming == SchedulePriority.High && _currentPriority == SchedulePriority.High && _currentItem is not null)
            {
                EnqueueItem(item, listName, "otro item ALTA sonando");
                return;
            }

            if (incoming == SchedulePriority.Low && _currentPriority == SchedulePriority.High && _currentItem is not null)
            {
                EnqueueItem(item, listName, "item ALTA sonando");
                return;
            }

            if (incoming == SchedulePriority.Low && _currentPriority == SchedulePriority.Low && _currentItem is not null)
            {
                EnqueueItem(item, listName, "otro item BAJA en curso");
                return;
            }

            StartItem(item, listName);
        }
    }

    private void EnqueueItem(ScheduledItem item, string listName, string reason)
    {
        if (_pendingQueue.Count >= MaxQueueSize)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"Cola llena ({MaxQueueSize}), se descarta '{item.DisplayTitle}'.");
            return;
        }

        _pendingQueue.Add(new PendingItem
        {
            Item = item,
            ListName = listName,
            EnqueuedAt = DateTime.Now
        });

        _scheduler.AddLog(SchedulerLogLevel.Info,
            $"'{item.DisplayTitle}' en cola ({reason}). Total: {_pendingQueue.Count}.");
    }

    private void ProcessQueue()
    {
        if (_currentItem is not null) return;

        while (_pendingQueue.Count > 0)
        {
            var next = _pendingQueue
                .OrderByDescending(p => p.Item.Priority)
                .ThenBy(p => p.EnqueuedAt)
                .FirstOrDefault();

            if (next is null) break;

            _pendingQueue.Remove(next);

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Sacando de cola: '{next.Item.DisplayTitle}' (quedan {_pendingQueue.Count}).");

            StartItem(next.Item, next.ListName);

            if (_currentItem is not null) return;
        }
    }

    private void ClearQueue()
    {
        if (_pendingQueue.Count == 0) return;

        _scheduler.AddLog(SchedulerLogLevel.Info,
            $"Limpiando cola ({_pendingQueue.Count} items pendientes).");

        _pendingQueue.Clear();
    }

    // ───── Punto de entrada de reproduccion ──────────────────────────────

    private void StartItem(ScheduledItem item, string listName)
    {
        if (item.Kind == ScheduledItemKind.Tanda)
        {
            // ── Nuevo modelo: la tanda vive en un archivo .tnd referenciado
            //    por FilePath. Prioridad: cargar del archivo.
            Tanda? tanda = null;

            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                try { tanda = TandaStore.Load(item.FilePath); }
                catch (Exception ex)
                {
                    _scheduler.AddLog(SchedulerLogLevel.Error,
                        $"Error cargando tanda '{Path.GetFileName(item.FilePath)}': {ex.Message}");
                }
            }

            // ── Retrocompatibilidad: si no hay FilePath valido pero el item
            //    tiene una Tanda embebida (de un .rms viejo), la usamos.
            if (tanda is null && item.EmbeddedTanda is not null)
            {
                tanda = item.EmbeddedTanda;
                _scheduler.AddLog(SchedulerLogLevel.Info,
                    $"'{item.DisplayTitle}': usando tanda embebida (formato antiguo). " +
                    "Recomendado: volver a guardarla como .tnd y referenciarla.");
            }

            if (tanda is null || tanda.Tracks.Count == 0)
            {
                _scheduler.AddLog(SchedulerLogLevel.Warning,
                    $"'{item.DisplayTitle}' no tiene una tanda valida (archivo no encontrado o vacia).");
                RaiseState(new PlaybackStateInfo(
                    PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                    "Tanda no disponible"));
                return;
            }

            var queue = tanda.Tracks.Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.FilePath)).ToList();
            if (queue.Count == 0)
            {
                _scheduler.AddLog(SchedulerLogLevel.Warning,
                    $"'{item.DisplayTitle}' no tiene pistas habilitadas con archivo.");
                RaiseState(new PlaybackStateInfo(
                    PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                    "Sin pistas validas"));
                return;
            }

            StopCurrentPlaybackInternal(unmarkScheduler: true);

            _currentItem = item;
            _currentListName = listName;
            _currentPriority = item.Priority;
            _currentTanda = tanda;
            _tandaQueue = queue;
            _tandaIndex = 0;

            _schedulerState.MarkPlaying(item.Id);

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Reproduciendo Tanda '{tanda.DisplayName}' ({queue.Count} pistas) desde '{listName}' ({(item.Priority == SchedulePriority.High ? "ALTA" : "Baja")}).");

            PlayTandaTrack(0, resumeAtSeconds: 0);
        }
        else if (item.Kind == ScheduledItemKind.Playlist)
        {
            StartPlaylistItem(item, listName);
        }
        else if (item.Kind == ScheduledItemKind.Block)
        {
            StartBlockItem(item, listName);
        }
        else
        {
            StopCurrentPlaybackInternal(unmarkScheduler: true);

            _currentItem = item;
            _currentListName = listName;
            _currentPriority = item.Priority;
            _currentTanda = null;
            _tandaQueue.Clear();
            _tandaIndex = 0;

            _schedulerState.MarkPlaying(item.Id);

            PlaySingleFile(item, listName, resumeAtSeconds: 0);
        }
    }

    // ───── Reproduccion de un item de tipo Playlist ──────────────────────

    private void StartPlaylistItem(ScheduledItem item, string listName)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' no tiene playlist asignada.");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Sin playlist"));
            return;
        }

        if (!File.Exists(item.FilePath))
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"Archivo de playlist no encontrado para '{item.DisplayTitle}': {item.FilePath}");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Playlist no encontrada"));
            return;
        }

        Playlist? playlist;
        try { playlist = PlaylistStore.Load(item.FilePath); }
        catch (Exception ex)
        {
            _scheduler.AddLog(SchedulerLogLevel.Error,
                $"Error cargando playlist '{Path.GetFileName(item.FilePath)}': {ex.Message}");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Error cargando playlist"));
            return;
        }

        if (playlist is null || playlist.Tracks.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' tiene una playlist vacia o no se pudo cargar.");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Playlist vacia"));
            return;
        }

        var tanda = ConvertPlaylistToTanda(playlist);

        // ===== FIX: FILTRO DE COLA PARA PLAYLISTS =====
        // Las playlists pueden tener locuciones como ítems sueltos (ItemKind =
        // Locution) que NO tienen FilePath. El filtro original descartaba todo
        // track sin FilePath, lo que borraba las locuciones antes de que
        // llegaran a PlayTandaTrack (donde el branch de locución las reproduce).
        //
        // Para .tnd nativas (StartItem) y bloques (StartBlockItem) el filtro
        // original sigue siendo correcto, porque esas entidades por diseño no
        // admiten locuciones. Solo se cambia aquí, en StartPlaylistItem.
        var queue = tanda.Tracks
            .Where(t => t.IsEnabled
                && (t.ItemKind == PlaylistItemKind.Locution
                    || !string.IsNullOrWhiteSpace(t.FilePath)))
            .ToList();

        if (queue.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' no tiene pistas habilitadas reproducibles.");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Sin pistas validas"));
            return;
        }

        StopCurrentPlaybackInternal(unmarkScheduler: true);

        _currentItem = item;
        _currentListName = listName;
        _currentPriority = item.Priority;
        _currentTanda = tanda;
        _tandaQueue = queue;
        _tandaIndex = 0;

        _schedulerState.MarkPlaying(item.Id);

        _scheduler.AddLog(SchedulerLogLevel.Info,
            $"Reproduciendo Playlist '{playlist.DisplayName}' ({queue.Count} pistas) desde '{listName}' ({(item.Priority == SchedulePriority.High ? "ALTA" : "Baja")}).");

        PlayTandaTrack(0, resumeAtSeconds: 0);
    }

    // ───── Reproduccion de un item de tipo Block ─────────────────────────

    /// <summary>
    /// Arranca un item de tipo Block. El bloque se "aplana" a una tanda
    /// virtual concatenando el contenido de todos sus items en orden.
    /// Despues se reusa el flujo normal de tanda (mixados incluidos).
    /// </summary>
    private void StartBlockItem(ScheduledItem item, string listName)
    {
        var block = item.EmbeddedBlock;

        if (block is null || block.Items.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' es un bloque vacio.");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Bloque vacio"));
            return;
        }

        // Aplanar el bloque a una tanda virtual.
        var tanda = ConvertBlockToTanda(block);

        if (tanda.Tracks.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' no tiene contenido reproducible (todos los items faltan o estan vacios).");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Bloque sin contenido"));
            return;
        }

        var queue = tanda.Tracks.Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.FilePath)).ToList();
        if (queue.Count == 0)
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"'{item.DisplayTitle}' no tiene pistas habilitadas con archivo.");
            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                "Sin pistas validas"));
            return;
        }

        StopCurrentPlaybackInternal(unmarkScheduler: true);

        _currentItem = item;
        _currentListName = listName;
        _currentPriority = item.Priority;
        _currentTanda = tanda;
        _tandaQueue = queue;
        _tandaIndex = 0;

        _schedulerState.MarkPlaying(item.Id);

        _scheduler.AddLog(SchedulerLogLevel.Info,
            $"Reproduciendo Bloque '{block.DisplayName}' ({queue.Count} pista(s) totales desde {block.Items.Count} item(s)) desde '{listName}' ({(item.Priority == SchedulePriority.High ? "ALTA" : "Baja")}).");

        PlayTandaTrack(0, resumeAtSeconds: 0);
    }

    /// <summary>
    /// Convierte las pistas habilitadas de una Playlist en una Tanda
    /// "virtual" que se puede pasar al flujo normal de tanda.
    /// </summary>
    private static Tanda ConvertPlaylistToTanda(Playlist playlist)
    {
        var tanda = new Tanda
        {
            Name = playlist.DisplayName,
            Tracks = new List<TandaTrack>()
        };

        foreach (var pt in playlist.Tracks)
        {
            if (!pt.IsEnabled) continue;

            // ===== LOCUCIONES COMO PISTA PRINCIPAL =====
            // Una Playlist puede tener locuciones como ítems sueltos
            // (ItemKind = Locution). No tienen archivo de audio, así que antes
            // se descartaban con el "if FilePath vacío → continue".
            // Ahora se agregan a la tanda virtual con ItemKind = Locution para
            // que PlayTandaTrack las reproduzca.
            if (pt.ItemKind == PlaylistItemKind.Locution)
            {
                var estimated = pt.EstimatedDurationSeconds > 0.5
                    ? pt.EstimatedDurationSeconds
                    : 5.0;

                tanda.Tracks.Add(new TandaTrack
                {
                    ItemKind = PlaylistItemKind.Locution,
                    FilePath = string.Empty,
                    LocutionCommand = pt.LocutionCommand,
                    EstimatedDurationSeconds = estimated,
                    Title = pt.DisplayTitle,
                    IsEnabled = true,
                    VolumeDb = pt.VolumeDb,
                    DurationSeconds = estimated,
                    DelayBeforeSeconds = 0
                });
                continue;
            }

            // Audio normal: requiere archivo.
            if (string.IsNullOrWhiteSpace(pt.FilePath)) continue;

            tanda.Tracks.Add(new TandaTrack
            {
                FilePath = pt.FilePath,
                Title = pt.Title,
                IsEnabled = true,
                VolumeDb = pt.VolumeDb,
                DurationSeconds = pt.DurationSeconds,
                DelayBeforeSeconds = 0,

                MixContentType = pt.MixContentType,
                MixAudioPath = pt.MixAudioPath,
                MixLocutionCommand = pt.MixLocutionCommand,
                MixStartSeconds = pt.MixStartSeconds,
                MixVolumeDb = pt.MixVolumeDb,
                MixDuckLevel = pt.MixDuckLevel
            });
        }
        return tanda;
    }

    /// <summary>
    /// Aplana un Bloque a una Tanda virtual concatenando el contenido de
    /// todos sus items en el orden en que estan definidos.
    ///
    /// Reglas de aplanado:
    ///   - SingleFile: agrega una pista con el archivo de audio.
    ///   - Playlist: carga el .rmf y agrega todas sus pistas habilitadas
    ///     (con los campos de mixado incluidos).
    ///   - Tanda: carga el .tnd y agrega todas sus pistas habilitadas
    ///     (con los campos de mixado incluidos).
    ///
    /// Los items que no se pueden cargar (archivo faltante, .rmf corrupto,
    /// etc) se SALTEAN con un warning en el log para no romper la reproduccion
    /// del resto del bloque.
    /// </summary>
    private Tanda ConvertBlockToTanda(ScheduledBlock block)
    {
        var tanda = new Tanda
        {
            Name = block.DisplayName,
            Tracks = new List<TandaTrack>()
        };

        foreach (var blockItem in block.Items)
        {
            if (string.IsNullOrWhiteSpace(blockItem.FilePath))
                continue;

            switch (blockItem.Kind)
            {
                case ScheduledItemKind.SingleFile:
                {
                    if (!File.Exists(blockItem.FilePath))
                    {
                        _scheduler.AddLog(SchedulerLogLevel.Warning,
                            $"Bloque '{block.DisplayName}': archivo no encontrado '{Path.GetFileName(blockItem.FilePath)}'. Se saltea.");
                        break;
                    }

                    tanda.Tracks.Add(new TandaTrack
                    {
                        FilePath = blockItem.FilePath,
                        Title = blockItem.Title,
                        IsEnabled = true
                    });
                    break;
                }

                case ScheduledItemKind.Playlist:
                {
                    if (!File.Exists(blockItem.FilePath))
                    {
                        _scheduler.AddLog(SchedulerLogLevel.Warning,
                            $"Bloque '{block.DisplayName}': playlist no encontrada '{Path.GetFileName(blockItem.FilePath)}'. Se saltea.");
                        break;
                    }

                    Playlist? pl = null;
                    try { pl = PlaylistStore.Load(blockItem.FilePath); } catch { }

                    if (pl is null || pl.Tracks.Count == 0)
                    {
                        _scheduler.AddLog(SchedulerLogLevel.Warning,
                            $"Bloque '{block.DisplayName}': playlist vacia o ilegible '{Path.GetFileName(blockItem.FilePath)}'. Se saltea.");
                        break;
                    }

                    var inner = ConvertPlaylistToTanda(pl);
                    tanda.Tracks.AddRange(inner.Tracks);
                    break;
                }

                case ScheduledItemKind.Tanda:
                {
                    if (!File.Exists(blockItem.FilePath))
                    {
                        _scheduler.AddLog(SchedulerLogLevel.Warning,
                            $"Bloque '{block.DisplayName}': tanda no encontrada '{Path.GetFileName(blockItem.FilePath)}'. Se saltea.");
                        break;
                    }

                    Tanda? td = null;
                    try { td = TandaStore.Load(blockItem.FilePath); } catch { }

                    if (td is null || td.Tracks.Count == 0)
                    {
                        _scheduler.AddLog(SchedulerLogLevel.Warning,
                            $"Bloque '{block.DisplayName}': tanda vacia o ilegible '{Path.GetFileName(blockItem.FilePath)}'. Se saltea.");
                        break;
                    }

                    foreach (var t in td.Tracks)
                    {
                        if (!t.IsEnabled) continue;
                        if (string.IsNullOrWhiteSpace(t.FilePath)) continue;

                        tanda.Tracks.Add(new TandaTrack
                        {
                            FilePath = t.FilePath,
                            Title = t.Title,
                            IsEnabled = true,
                            VolumeDb = t.VolumeDb,
                            DurationSeconds = t.DurationSeconds,
                            DelayBeforeSeconds = t.DelayBeforeSeconds,

                            MixContentType = t.MixContentType,
                            MixAudioPath = t.MixAudioPath,
                            MixLocutionCommand = t.MixLocutionCommand,
                            MixStartSeconds = t.MixStartSeconds,
                            MixVolumeDb = t.MixVolumeDb,
                            MixDuckLevel = t.MixDuckLevel
                        });
                    }
                    break;
                }
            }
        }

        return tanda;
    }

    // ───── Reproduccion de SingleFile ────────────────────────────────────

    private void PlaySingleFile(ScheduledItem item, string listName, double resumeAtSeconds)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            AppLogger.Warning("SCHEDULER",
                $"Item '{item.DisplayTitle}' no tiene archivo asignado.");
            _schedulerState.UnmarkPlaying(item.Id);
            RaiseState(PlaybackStateInfo.Idle);
            return;
        }

        if (!File.Exists(item.FilePath))
        {
            AppLogger.Warning("SCHEDULER",
                $"Archivo no encontrado para item '{item.DisplayTitle}': {item.FilePath}");
            _schedulerState.UnmarkPlaying(item.Id);
            RaiseState(PlaybackStateInfo.Idle);
            return;
        }

        var bus = _targetBus ?? _audioEngine.PrimaryBus;

        try
        {
            var channel = bus.LoadFile(item.FilePath);

            // ===== EXCLUSIVIDAD DE BUS =====
            var acquire = TryAcquireBus(bus.Id, channel);
            if (acquire.Denied)
            {
                AppLogger.Warning("SCHEDULER",
                    $"Item '{item.DisplayTitle}' rechazado: bus '{bus.DisplayName}' ocupado por mayor prioridad.");
                try { channel.Dispose(); } catch { }
                _schedulerState.UnmarkPlaying(item.Id);
                RaiseState(PlaybackStateInfo.Idle);
                return;
            }

            _currentChannel = channel;
            _currentFilePath = item.FilePath;

            channel.Ended += OnChannelEnded;

            var deviceName = bus.DeviceDisplayName;
            var fileName = Path.GetFileName(item.FilePath);
            var priority = item.Priority == SchedulePriority.High ? "ALTA" : "Baja";

            AppLogger.Info("SCHEDULER PLAY",
                $"Device: {deviceName} | " +
                $"Item: {item.DisplayTitle} | " +
                $"Prioridad: {priority} | " +
                $"Lista: {listName} | " +
                $"Reproduciendo: {fileName}" +
                (resumeAtSeconds > 0.1 ? $" (desde {resumeAtSeconds:F1}s)" : ""));

            if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
            {
                AppLogger.Info("SCHEDULER",
                    $"FADE activado para {fileName} | Duracion: {_autoFadeSeconds:F1}s");
            }
            else
            {
                AppLogger.Info("SCHEDULER", $"FADE desactivado para {fileName}");
            }

            try { ConfigureChannel?.Invoke(channel, item.FilePath, true); } catch { }

            if (resumeAtSeconds > 0.1)
            {
                channel.Seek(TimeSpan.FromSeconds(resumeAtSeconds));
                channel.FadeIn(50, 1.0, preservePosition: true);
            }
            else if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
            {
                channel.SetAutoFade(true, _autoFadeSeconds);
                channel.FadeIn((int)(_autoFadeSeconds * 1000), 1.0);
            }
            else if (acquire.ExpropriatedOwnerId is not null && _coordinator is not null)
            {
                // Expropiamos a otro modulo: fade-in suave.
                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);
                channel.FadeIn(ms, 1.0);
            }
            else
            {
                channel.Play();
            }

            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Playing,
                item.DisplayTitle, listName, item.Priority,
                resumeAtSeconds, channel.Duration.TotalSeconds, null));

            StartProgressTimer();
        }
        catch (Exception ex)
        {
            _scheduler.AddLog(SchedulerLogLevel.Error,
                $"Error reproduciendo '{item.DisplayTitle}': {ex.Message}");

            _currentChannel = null;
            _currentItem = null;
            _currentFilePath = string.Empty;

            _schedulerState.UnmarkPlaying(item.Id);

            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Error, item.DisplayTitle, listName, item.Priority, 0, 0,
                ex.Message));
        }
    }

    // ───── Reproduccion de pistas de una tanda/playlist/bloque ─────────

    private void PlayTandaTrack(int index, double resumeAtSeconds)
    {
        if (_currentTanda is null || _currentItem is null)
            return;

        var currentTanda = _currentTanda;
        var currentItem = _currentItem;

        if (index < 0 || index >= _tandaQueue.Count)
        {
            OnTandaFinished();
            return;
        }

        var track = _tandaQueue[index];
        _tandaIndex = index;

        // ===== LOCUCIONES COMO PISTA PRINCIPAL =====
        // Las locuciones no tienen archivo de audio, por eso van antes del
        // chequeo de FilePath. Solo aparecen en tandas virtuales creadas
        // desde una Playlist programada.
        if (track.ItemKind == PlaylistItemKind.Locution)
        {
            PlayLocutionInTanda(index, track);
            return;
        }

        if (string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
        {
            _scheduler.AddLog(SchedulerLogLevel.Warning,
                $"Pista {index + 1} de '{currentTanda.DisplayName}' no tiene archivo valido. Se saltea.");
            PlayNextTandaTrack();
            return;
        }

        var bus = _targetBus ?? _audioEngine.PrimaryBus;

        try
        {
            var channel = bus.LoadFile(track.FilePath);

            // ===== EXCLUSIVIDAD DE BUS =====
            var acquire = TryAcquireBus(bus.Id, channel);
            if (acquire.Denied)
            {
                AppLogger.Warning("SCHEDULER",
                    $"Pista {index + 1} rechazada: bus '{bus.DisplayName}' ocupado por mayor prioridad.");
                try { channel.Dispose(); } catch { }
                PlayNextTandaTrack();
                return;
            }

            _currentChannel = channel;
            _currentFilePath = track.FilePath;

            channel.Ended += OnChannelEnded;

            var deviceName = bus.DeviceDisplayName;
            var fileName = Path.GetFileName(track.FilePath);
            var tandaName = currentTanda.DisplayName;
            int trackNum = index + 1;
            int trackTotal = _tandaQueue.Count;

            AppLogger.Info("SCHEDULER PLAY",
                $"Device: {deviceName} | " +
                $"Tanda/Bloque: {tandaName} | " +
                $"Pista {trackNum}/{trackTotal} | " +
                $"Reproduciendo: {fileName}" +
                (track.DelayBeforeSeconds > 0.05 ? $" | Espera previa: {track.DelayBeforeSeconds:F1}s" : "") +
                (track.HasMix ? $" | MIX: {Path.GetFileName(track.MixAudioPath)} @ {track.MixStartSeconds:F1}s" : ""));

            if (_autoFadeEnabled && _autoFadeSeconds > 0.05 && index == 0)
            {
                AppLogger.Info("SCHEDULER",
                    $"FADE activado para {fileName} (primera pista) | Duracion: {_autoFadeSeconds:F1}s");
            }

            try { ConfigureChannel?.Invoke(channel, track.FilePath, true); } catch { }

            double trackGainDb = track.VolumeDb;
            if (Math.Abs(trackGainDb) > 0.01)
            {
                try
                {
                    double currentNorm = channel.NormalizationGainDb;
                    channel.SetNormalizationGain(currentNorm + trackGainDb);
                }
                catch { }
            }

            if (resumeAtSeconds > 0.1)
            {
                channel.Seek(TimeSpan.FromSeconds(resumeAtSeconds));
                channel.FadeIn(50, 1.0, preservePosition: true);
            }
            else if (_autoFadeEnabled && _autoFadeSeconds > 0.05 && index == 0)
            {
                channel.SetAutoFade(true, _autoFadeSeconds);
                channel.FadeIn((int)(_autoFadeSeconds * 1000), 1.0);
            }
            else if (acquire.ExpropriatedOwnerId is not null && _coordinator is not null && index == 0)
            {
                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);
                channel.FadeIn(ms, 1.0);
            }
            else
            {
                channel.Play();
            }

            // Precalentar el analisis de la proxima pista MIENTRAS suena la actual.
            try
            {
                if (index + 1 < _tandaQueue.Count)
                {
                    var nextTrack = _tandaQueue[index + 1];
                    if (!string.IsNullOrWhiteSpace(nextTrack.FilePath) &&
                        File.Exists(nextTrack.FilePath))
                    {
                        PrewarmFile?.Invoke(nextTrack.FilePath);
                    }
                }
            }
            catch { }

            // Programar el mixado intermedio si la pista lo tiene.
            // Ahora incluye locuciones además de audio. StartMixPlayback decide
            // qué hacer según MixContentType.
            if (track.HasMix)
            {
                StartMixTimer(track);
            }

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"'{currentTanda.DisplayName}': pista {index + 1}/{_tandaQueue.Count} — '{track.DisplayTitle}'.");

            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Playing,
                currentItem.DisplayTitle, _currentListName, _currentPriority,
                resumeAtSeconds, channel.Duration.TotalSeconds, null,
                TandaName: currentTanda.DisplayName,
                TandaTrackIndex: index,
                TandaTrackCount: _tandaQueue.Count,
                TandaTrackTitle: track.DisplayTitle));

            StartProgressTimer();
        }
        catch (Exception ex)
        {
            _scheduler.AddLog(SchedulerLogLevel.Error,
                $"Error reproduciendo pista {index + 1} de '{currentTanda.DisplayName}': {ex.Message}");

            _currentChannel = null;
            _currentFilePath = string.Empty;

            PlayNextTandaTrack();
        }
    }

    private void PlayNextTandaTrack()
    {
        if (_currentItem is null || _currentTanda is null) return;

        int nextIndex = _tandaIndex + 1;

        if (nextIndex >= _tandaQueue.Count)
        {
            OnTandaFinished();
            return;
        }

        var nextTrack = _tandaQueue[nextIndex];
        double delay = Math.Max(0, nextTrack.DelayBeforeSeconds);

        if (delay > 0.05)
        {
            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Esperando {delay:F1}s antes de la pista {nextIndex + 1}...");

            StartTandaDelayTimer(delay, () => PlayTandaTrack(nextIndex, 0));
        }
        else
        {
            PlayTandaTrack(nextIndex, 0);
        }
    }

    private void OnTandaFinished()
    {
        if (_currentItem is null) return;

        var item = _currentItem;
        var title = item.DisplayTitle;

        StopProgressTimer();
        StopTandaDelayTimer();

        StopMixTimer();
        StopMixChannel();
        _mixOriginalNormGainDb = 0;

        try
        {
            if (_currentChannel is not null)
            {
                _currentChannel.Ended -= OnChannelEnded;
                _currentChannel.Stop();
                _currentChannel.Dispose();
                _currentChannel = null;
            }
        }
        catch { }

        _currentItem = null;
        _currentTanda = null;
        _tandaQueue.Clear();
        _tandaIndex = 0;
        _currentPriority = SchedulePriority.Low;
        _currentListName = string.Empty;
        _currentFilePath = string.Empty;

        _schedulerState.UnmarkPlaying(item.Id);

        _scheduler.AddLog(SchedulerLogLevel.Info, $"Termino '{title}'.");

        // Prioridad: primero cola, despues suspendido, despues Idle.
        if (_pendingQueue.Count > 0)
        {
            ProcessQueue();

            // Si la cola no arranco nada (todos fallaron), caer al fallback.
            if (_currentItem is null)
            {
                if (_suspended is not null)
                    ResumeSuspended();
                else
                    FinishPlayback();
            }
        }
        else if (_suspended is not null)
        {
            ResumeSuspended();
        }
        else
        {
            FinishPlayback();
        }
    }

    /// <summary>
    /// Termina la reproduccion actual y notifica al UI. Si habia un Play
    /// manual en curso, limpia el flag y emite ManualPlayFinished.
    /// </summary>
    private void FinishPlayback()
    {
        if (_isManualPlayInProgress)
        {
            _isManualPlayInProgress = false;

            try { ManualPlayFinished?.Invoke(this, EventArgs.Empty); }
            catch { }
        }

        RaiseState(PlaybackStateInfo.Idle);
    }

    // ───── Mixado intermedio ─────────────────────────────────────────────

    private void StartMixTimer(TandaTrack track)
    {
        StopMixTimer();

        double trigger = Math.Max(0, track.MixStartSeconds);

        if (trigger <= 0.05)
        {
            _dispatch(StartMixPlayback);
            return;
        }

        _mixTimer = new System.Threading.Timer(
            _ => _dispatch(StartMixPlayback),
            null,
            dueTime: (int)(trigger * 1000),
            period: System.Threading.Timeout.Infinite);
    }

    private void StopMixTimer()
    {
        try { _mixTimer?.Dispose(); } catch { }
        _mixTimer = null;
    }

    private void StartMixPlayback()
    {
        if (_currentItem is null) return;
        if (_tandaIndex < 0 || _tandaIndex >= _tandaQueue.Count) return;

        var track = _tandaQueue[_tandaIndex];
        var mainChannel = _currentChannel;

        if (track is null || mainChannel is null) return;
        if (!track.HasMix) return;

        // Locución: deriva al runner asíncrono.
        if (track.MixContentType == MixContentType.Locution)
        {
            _ = StartLocutionMixAsync(track, mainChannel);
            return;
        }

        // Audio mix (comportamiento clásico).
        if (string.IsNullOrWhiteSpace(track.MixAudioPath)) return;
        if (!File.Exists(track.MixAudioPath)) return;

        var bus = _targetBus ?? _audioEngine.PrimaryBus;

        try
        {
            _mixOriginalNormGainDb = mainChannel.NormalizationGainDb;

            double duckDb = 20.0 * Math.Log10(Math.Max(0.001, track.MixDuckLevel));
            mainChannel.SetNormalizationGain(_mixOriginalNormGainDb + duckDb);

            var mixChannel = bus.LoadFile(track.MixAudioPath);
            _mixChannel = mixChannel;

            try { ConfigureChannel?.Invoke(mixChannel, track.MixAudioPath, true); } catch { }

            mixChannel.Ended += OnMixChannelEnded;

            double baseNorm = mixChannel.NormalizationGainDb;
            mixChannel.SetNormalizationGain(baseNorm + track.MixVolumeDb);

            mixChannel.Play();

            AppLogger.Info("SCHEDULER PLAY",
                $"MIX iniciado: {Path.GetFileName(track.MixAudioPath)} | " +
                $"Volumen adicional: {track.MixVolumeDb:+0.0;-0.0;0.0}dB | " +
                $"Principal reducida a: {track.MixDuckLevel:P0}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error iniciando mixado: {ex.Message}");
            RestoreMainVolume();
        }
    }

    /// <summary>
    /// Ejecuta una locución de RMVoice como mixado intermedio de una pista
    /// de tanda. Equivalente a PlaylistPlayer.StartLocutionMixAsync.
    /// </summary>
    private async Task StartLocutionMixAsync(TandaTrack track, IAudioChannel mainChannel)
    {
        if (_locution is null || !_locution.IsAvailable)
        {
            AppLogger.Warning("SCHEDULER PLAY",
                "Locución solicitada pero RMVoice no está disponible. " +
                "El audio principal continúa sin interrupción.");
            return;
        }

        int deviceIndex = ResolveCurrentBusDevice();

        _mixOriginalNormGainDb = mainChannel.NormalizationGainDb;
        double duckDb = 20.0 * Math.Log10(Math.Max(0.001, track.MixDuckLevel));
        mainChannel.SetNormalizationGain(_mixOriginalNormGainDb + duckDb);

        AppLogger.Info("SCHEDULER PLAY",
            $"LOCUCIÓN MIX iniciada: {track.MixLocutionCommand} | " +
            $"Device: {deviceIndex} | " +
            $"Principal reducida a: {track.MixDuckLevel:P0}");

        CancellationTokenSource? locutionCts = null;
        try
        {
            // Volumen pleno para locuciones (mismo fix que PlaylistPlayer:
            // MixVolumeDb fue diseñado para jingles de fondo, no para voz).
            float volumeLinear = 1.0f;

            locutionCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _mixLocutionCts = locutionCts;
            await _locution.PlayAsync(track.MixLocutionCommand, deviceIndex, volumeLinear, locutionCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warning("SCHEDULER PLAY",
                $"Locución {track.MixLocutionCommand} cancelada por timeout (45s).");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("SCHEDULER PLAY",
                $"Error en locución {track.MixLocutionCommand}: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_mixLocutionCts, locutionCts))
                _mixLocutionCts = null;
            locutionCts?.Dispose();
            try
            {
                mainChannel.SetNormalizationGain(_mixOriginalNormGainDb);
            }
            catch { }

            AppLogger.Info("SCHEDULER PLAY",
                $"LOCUCIÓN MIX finalizada: {track.MixLocutionCommand}");
        }
    }

    /// <summary>
    /// Devuelve el índice REAL de BASS del bus actual. Es el device al que
    /// se manda la locución para que suene por el mismo canal que la pista.
    /// </summary>
    private int ResolveCurrentBusDevice()
    {
        try
        {
            var bus = _targetBus ?? _audioEngine.PrimaryBus;
            return bus.ResolvedDeviceIndex;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reproduce una locución como pista principal de una tanda virtual.
    /// Adaptación de PlaylistPlayer.PlayLocutionTrack para TandaTrack.
    /// No carga canal de audio: dispara el comando de RMVoice async y,
    /// al terminar, avanza a la siguiente pista.
    /// </summary>
    private void PlayLocutionInTanda(int index, TandaTrack track)
    {
        var currentTanda = _currentTanda;
        var currentItem = _currentItem;
        if (currentTanda is null || currentItem is null) return;

        // Detener cualquier estado previo.
        StopProgressTimer();
        StopMixTimer();
        StopMixChannel();
        _mixOriginalNormGainDb = 0;

        // Las locuciones no tienen canal de audio asociado.
        try
        {
            if (_currentChannel is not null)
            {
                _currentChannel.Ended -= OnChannelEnded;
                _currentChannel.Stop();
                _currentChannel.Dispose();
            }
        }
        catch { }
        _currentChannel = null;
        _currentFilePath = string.Empty;

        // Resolver device del bus actual.
        var bus = _targetBus ?? _audioEngine.PrimaryBus;
        int deviceIndex = 0;
        try { deviceIndex = bus.ResolvedDeviceIndex; } catch { }

        // Volumen de la pista (dB → lineal).
        double volumeLinear = Math.Pow(10.0, track.VolumeDb / 20.0);
        float volume = (float)Math.Clamp(volumeLinear, 0.0, 1.0);

        _tandaLocutionPaused = false;
        var locutionCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _tandaLocutionCts = locutionCts;

        _scheduler.AddLog(SchedulerLogLevel.Info,
            $"'{currentTanda.DisplayName}': pista {index + 1}/{_tandaQueue.Count} — LOCUCIÓN: {track.DisplayTitle}.");

        double estimatedDur = track.EstimatedDurationSeconds > 0.5
            ? track.EstimatedDurationSeconds
            : track.DurationSeconds;

        RaiseState(new PlaybackStateInfo(
            PlaybackStateKind.Playing,
            currentItem.DisplayTitle, _currentListName, _currentPriority,
            0, estimatedDur, null,
            TandaName: currentTanda.DisplayName,
            TandaTrackIndex: index,
            TandaTrackCount: _tandaQueue.Count,
            TandaTrackTitle: track.DisplayTitle));

        _ = RunLocutionInTandaAsync(index, track, deviceIndex, volume, locutionCts);
    }

    /// <summary>
    /// Ejecuta la locución async y, al terminar, avanza a la siguiente
    /// pista de la tanda.
    /// </summary>
    private async Task RunLocutionInTandaAsync(
        int index,
        TandaTrack track,
        int deviceIndex,
        float volume,
        CancellationTokenSource cts)
    {
        try
        {
            if (_locution is not null && _locution.IsAvailable)
            {
                await _locution.PlayAsync(track.LocutionCommand, deviceIndex, volume, cts.Token);
            }
            else
            {
                AppLogger.Warning("SCHEDULER PLAY",
                    "Locución solicitada pero RMVoice no está disponible. Se saltea.");
                await Task.Delay(500, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelada por StopAll/Next/etc: no avanzar.
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Error("SCHEDULER PLAY",
                $"Error reproduciendo locución '{track.DisplayTitle}': {ex.Message}", ex);
            // Igual avanzar para no dejar la tanda trabada.
        }
        finally
        {
            if (ReferenceEquals(_tandaLocutionCts, cts))
                _tandaLocutionCts = null;
            cts.Dispose();
        }

        _dispatch(() =>
        {
            lock (_gate)
            {
                // Verificar que seguimos en la misma posición. Si el usuario
                // saltó o detuvo, _tandaIndex ya no coincide.
                if (_tandaIndex != index) return;
                if (_currentTanda is null) return;

                AppLogger.Info("SCHEDULER PLAY",
                    $"LOCUCIÓN finalizada: {track.DisplayTitle}");

                PlayNextTandaTrack();
            }
        });
    }

    private void OnMixChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        _dispatch(() =>
        {
            StopMixChannel();
            RestoreMainVolume();
        });
    }

    private void StopMixChannel()
    {
        CancelMixLocution();
        if (_mixChannel is null) return;

        try
        {
            _mixChannel.Ended -= OnMixChannelEnded;
            _mixChannel.Stop();
            _mixChannel.Dispose();
        }
        catch { }
        _mixChannel = null;
    }

    private void CancelMixLocution()
    {
        var cts = _mixLocutionCts;
        _mixLocutionCts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        _locution?.Stop();
    }

    private void CancelTandaLocution()
    {
        var cts = _tandaLocutionCts;
        _tandaLocutionCts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        _locution?.Stop();
    }

    private void RestoreMainVolume()
    {
        var mainChannel = _currentChannel;
        if (mainChannel is null) return;

        try
        {
            mainChannel.SetNormalizationGain(_mixOriginalNormGainDb);
        }
        catch { }
    }

    // ───── Timer para delays entre pistas ────────────────────────────────

    private void StartTandaDelayTimer(double seconds, Action callback)
    {
        StopTandaDelayTimer();

        _tandaDelayTimer = new System.Threading.Timer(
            _ => _dispatch(() => { StopTandaDelayTimer(); callback(); }),
            null,
            dueTime: (int)(seconds * 1000),
            period: System.Threading.Timeout.Infinite);
    }

    private void StopTandaDelayTimer()
    {
        try { _tandaDelayTimer?.Dispose(); } catch { }
        _tandaDelayTimer = null;
    }

    // ───── Fin de una pista (SingleFile o de tanda/playlist/bloque) ────

    private void OnChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_currentChannel is null) return;

                StopProgressTimer();

                StopMixTimer();
                StopMixChannel();
                _mixOriginalNormGainDb = 0;

                try
                {
                    _currentChannel.Ended -= OnChannelEnded;
                    _currentChannel.Dispose();
                }
                catch { }

                _currentChannel = null;
                _currentFilePath = string.Empty;

                if (_currentTanda is not null && _currentItem is not null && _tandaQueue.Count > 0)
                {
                    AppLogger.Info("SCHEDULER PLAY",
                        $"Fin de pista: {_tandaQueue[_tandaIndex].DisplayTitle}");
                    PlayNextTandaTrack();
                    return;
                }

                var endedTitle = _currentItem?.DisplayTitle ?? "?";
                var endedId = _currentItem?.Id;

                AppLogger.Info("SCHEDULER PLAY", $"Fin de reproduccion programada: {endedTitle}");

                _currentItem = null;
                _currentPriority = SchedulePriority.Low;
                _currentListName = string.Empty;

                if (!string.IsNullOrEmpty(endedId))
                    _schedulerState.UnmarkPlaying(endedId);

                _scheduler.AddLog(SchedulerLogLevel.Info, $"Termino '{endedTitle}'.");

                if (_pendingQueue.Count > 0)
                {
                    ProcessQueue();

                    if (_currentItem is null)
                    {
                        if (_suspended is not null)
                            ResumeSuspended();
                        else
                            FinishPlayback();
                    }
                }
                else if (_suspended is not null)
                {
                    ResumeSuspended();
                }
                else
                {
                    FinishPlayback();
                }
            }
        });
    }

    // ───── Suspension y reanudacion ──────────────────────────────────────

    private void SuspendCurrent()
    {
        if (_currentItem is null) return;

        // RMVoice no puede conservar la posición de una locución. Al reanudar
        // la tanda se volverá a iniciar desde el comienzo de esta pista.
        CancelTandaLocution();
        _tandaLocutionPaused = false;

        try
        {
            double posSec = 0;
            double durSec = 0;

            if (_currentChannel is not null)
            {
                try
                {
                    posSec = _currentChannel.Position.TotalSeconds;
                    durSec = _currentChannel.Duration.TotalSeconds;
                }
                catch { }
            }

            // Guardar siempre la tanda activa. Una tanda de tipo .tnd puede
            // no estar embebida en ScheduledItem y debe sobrevivir la pausa.
            Tanda? virtualTanda = _currentTanda;

            _suspended = new SuspendedState
            {
                Item = _currentItem,
                ListName = _currentListName,
                TandaIndex = _currentTanda is not null ? _tandaIndex : -1,
                TandaQueue = new List<TandaTrack>(_tandaQueue),
                PositionSeconds = posSec,
                DurationSeconds = durSec,
                VirtualTanda = virtualTanda
            };

            StopTandaDelayTimer();
            StopProgressTimer();

            StopMixTimer();
            StopMixChannel();
            _mixOriginalNormGainDb = 0;

            if (_currentChannel is not null)
            {
                try
                {
                    _currentChannel.Ended -= OnChannelEnded;
                    _currentChannel.Stop();
                    _currentChannel.Dispose();
                }
                catch { }
                _currentChannel = null;
            }

            var suspendedTitle = _suspended.Item.DisplayTitle;
            var tandaInfo = _suspended.TandaIndex >= 0
                ? $" (pista {_suspended.TandaIndex + 1})"
                : string.Empty;

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Suspendido '{suspendedTitle}'{tandaInfo} en {posSec:F1}s (sera reanudado).");

            _currentItem = null;
            _currentTanda = null;
            _tandaQueue.Clear();
            _tandaIndex = 0;
            _currentPriority = SchedulePriority.Low;
            _currentListName = string.Empty;
            _currentFilePath = string.Empty;

            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Suspended,
                suspendedTitle, _suspended.ListName, SchedulePriority.Low,
                posSec, durSec, null,
                TandaName: _suspended.Item.EmbeddedTanda?.DisplayName
                           ?? _suspended.Item.EmbeddedBlock?.DisplayName
                           ?? _suspended.VirtualTanda?.DisplayName
                           ?? string.Empty,
                TandaTrackIndex: _suspended.TandaIndex,
                TandaTrackCount: _suspended.TandaQueue.Count,
                TandaTrackTitle: _suspended.TandaIndex >= 0 && _suspended.TandaIndex < _suspended.TandaQueue.Count
                    ? _suspended.TandaQueue[_suspended.TandaIndex].DisplayTitle
                    : string.Empty));
        }
        catch (Exception ex)
        {
            _scheduler.AddLog(SchedulerLogLevel.Error,
                $"Error suspendiendo canal actual: {ex.Message}");
            _suspended = null;
        }
    }

    private void ResumeSuspended()
    {
        if (_suspended is null) return;

        var suspended = _suspended;
        _suspended = null;

        _currentItem = suspended.Item;
        _currentListName = suspended.ListName;
        _currentPriority = suspended.Item.Priority;

        if (suspended.TandaIndex >= 0)
        {
            _currentTanda = suspended.VirtualTanda
                            ?? suspended.Item.EmbeddedTanda;
            _tandaQueue = new List<TandaTrack>(suspended.TandaQueue);
            _tandaIndex = suspended.TandaIndex;

            if (_currentTanda is null || _tandaQueue.Count == 0)
            {
                _scheduler.AddLog(SchedulerLogLevel.Error,
                    $"No se pudo reanudar '{suspended.Item.DisplayTitle}' (tanda no disponible).");
                _schedulerState.UnmarkPlaying(suspended.Item.Id);
                _currentItem = null;
                RaiseState(PlaybackStateInfo.Idle);
                return;
            }

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Reanudando '{_currentTanda.DisplayName}' desde pista {_tandaIndex + 1} en {suspended.PositionSeconds:F1}s.");

            PlayTandaTrack(_tandaIndex, suspended.PositionSeconds);
        }
        else
        {
            _currentTanda = null;
            _tandaQueue.Clear();
            _tandaIndex = 0;

            _scheduler.AddLog(SchedulerLogLevel.Info,
                $"Reanudando '{suspended.Item.DisplayTitle}' desde {suspended.PositionSeconds:F1}s.");

            PlaySingleFile(suspended.Item, suspended.ListName, suspended.PositionSeconds);
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void StopCurrentPlaybackInternal(bool unmarkScheduler)
    {
        CancelTandaLocution();
        _tandaLocutionPaused = false;
        StopProgressTimer();
        StopTandaDelayTimer();

        StopMixTimer();
        StopMixChannel();
        _mixOriginalNormGainDb = 0;

        var itemId = _currentItem?.Id;

        try
        {
            if (_currentChannel is not null)
            {
                _currentChannel.Ended -= OnChannelEnded;
                _currentChannel.Stop();
                _currentChannel.Dispose();
            }
        }
        catch { }

        _currentChannel = null;
        _currentItem = null;
        _currentTanda = null;
        _tandaQueue.Clear();
        _tandaIndex = 0;
        _currentPriority = SchedulePriority.Low;
        _currentListName = string.Empty;
        _currentFilePath = string.Empty;

        if (unmarkScheduler && !string.IsNullOrEmpty(itemId))
            _schedulerState.UnmarkPlaying(itemId);
    }

    private void StartProgressTimer()
    {
        StopProgressTimer();

        _progressTimer = new System.Threading.Timer(
            _ => EmitProgressTick(),
            null,
            dueTime: 200,
            period: 200);
    }

    private void StopProgressTimer()
    {
        try { _progressTimer?.Dispose(); } catch { }
        _progressTimer = null;
    }

    private void EmitProgressTick()
    {
        try
        {
            var ch = _currentChannel;
            var item = _currentItem;

            if (ch is null || item is null) return;

            double pos = 0;
            double dur = 0;
            try
            {
                pos = ch.Position.TotalSeconds;
                dur = ch.Duration.TotalSeconds;
            }
            catch { return; }

            RaiseState(new PlaybackStateInfo(
                PlaybackStateKind.Playing,
                item.DisplayTitle, _currentListName, _currentPriority,
                pos, dur, null,
                TandaName: _currentTanda?.DisplayName ?? string.Empty,
                TandaTrackIndex: _currentTanda is not null ? _tandaIndex : -1,
                TandaTrackCount: _tandaQueue.Count,
                TandaTrackTitle: _currentTanda is not null && _tandaIndex < _tandaQueue.Count
                    ? _tandaQueue[_tandaIndex].DisplayTitle
                    : string.Empty));
        }
        catch { }
    }

    private void RaiseState(PlaybackStateInfo state)
    {
        // ===== EXCLUSIVIDAD DE BUS =====
        // Cuando el scheduler termina completamente (Idle), libera el bus
        // para que el coordinator pueda reanudar al suspendido (si lo hay).
        if (state.Kind == PlaybackStateKind.Idle && !string.IsNullOrEmpty(_acquiredBusId))
        {
            ReleaseBusInternal();
        }

        try { StateChanged?.Invoke(this, state); } catch { }
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        try { _scheduler.ItemTriggered -= OnItemTriggered; } catch { }
        if (_coordinator is not null)
            try { _coordinator.ChannelExpropriated -= OnChannelExpropriated; } catch { }

        try { StopCurrentPlaybackInternal(unmarkScheduler: true); } catch { }

        if (_suspended is not null && !string.IsNullOrEmpty(_suspended.Item.Id))
            _schedulerState.UnmarkPlaying(_suspended.Item.Id);

        _suspended = null;

        ReleaseBusInternal();
    }

    // ===== EXCLUSIVIDAD DE BUS =====

    private BusAcquireResult TryAcquireBus(string busId, IAudioChannel channel)
    {
        if (_coordinator is null) return BusAcquireResult.GrantedNoConflict;

        if (!string.IsNullOrEmpty(_acquiredBusId) &&
            string.Equals(_acquiredBusId, busId, StringComparison.OrdinalIgnoreCase))
            return BusAcquireResult.GrantedNoConflict;

        if (!string.IsNullOrEmpty(_acquiredBusId))
            ReleaseBusInternal();

        var result = _coordinator.TryAcquire(busId, _instanceId, ModuleRole.Scheduler, channel);
        if (result.Granted)
            _acquiredBusId = busId;
        return result;
    }

    private void ReleaseBusInternal()
    {
        if (_coordinator is null) return;
        if (string.IsNullOrEmpty(_acquiredBusId)) return;
        try { _coordinator.Release(_acquiredBusId, _instanceId); } catch { }
        _acquiredBusId = string.Empty;
    }

    private void OnChannelExpropriated(object? sender, ChannelExpropriatedEventArgs e)
    {
        if (!string.Equals(e.OwnerId, _instanceId, StringComparison.Ordinal)) return;

        // El scheduler tiene prioridad Air (la mas alta). No deberia ser
        // expropiado salvo que el usuario haya cambiado las prioridades en
        // configuracion. Defensivo: loggear y detener.
        _dispatch(() =>
        {
            lock (_gate)
            {
                _acquiredBusId = string.Empty;
                AppLogger.Warning("SCHEDULER",
                    $"Scheduler expropiado del bus '{e.BusId}' por '{e.Role}'. " +
                    "Deteniendo reproduccion programada.");
                StopCurrentPlaybackInternal(unmarkScheduler: true);
                RaiseState(PlaybackStateInfo.Idle);
            }
        });
    }

    public sealed record QueuedItemInfo(
        string Title,
        string ListName,
        SchedulePriority Priority,
        bool IsTanda,
        int TandaTrackCount);
}
