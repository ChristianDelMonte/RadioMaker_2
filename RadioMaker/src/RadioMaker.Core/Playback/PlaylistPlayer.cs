using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Scheduling;
using RadioMaker.Core.Logging;
using System.Threading;

namespace RadioMaker.Core.Playback;

/// -------------
/// Motor de reproduccion de playlists.
///
/// Es un singleton por aplicacion: solo puede sonar una playlist a la vez.
/// Cuando se arranca una nueva, la anterior se detiene automaticamente.
///
/// Uso desde la UI:
///   - Play(playlist, startIndex) → comienza desde la pista indicada.
///   - Pause / Resume → pausa y reanuda manualmente.
///   - Stop() → detiene todo.
///   - Next() / Prev() → salta de pista.
///
/// Modos:
///   - Once: reproduce hasta el final y para.
///   - Loop: al terminar la ultima pista vuelve a la primera.
///   - Shuffle: elige pistas al azar (sin repeticion hasta agotar todas).
///
/// Procesamiento automatico:
///   Antes de cada Play, se llama al ConfigureChannel hook (si esta seteado)
///   para aplicar EQ, Trim y Normalizacion del archivo.
///
/// Coordinacion con el scheduler:
///   El BusExclusivityCoordinator se encarga de pausar esta playlist cuando
///   el scheduler arranca y de reanudarla cuando termina, a traves de los
///   eventos ChannelExpropriated / ChannelResumed.
/// -------------
public sealed class PlaylistPlayer : IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly Action<Action> _dispatch;
    private readonly LocutionService? _locution;
    private readonly BusExclusivityCoordinator? _coordinator;
    private readonly EncadenadoresRegistry? _encadenadores;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private string _acquiredBusId = string.Empty;

    private readonly object _gate = new();

    // Crossfade entre pistas (0 = desactivado).
    private double _crossfadeSeconds;
    private bool _isCrossfading;
    private bool _disposed;

    // ───── Configuracion ─────────────────────────────────────────────────

    private IAudioBus? _targetBus;
    private PlaylistInterruptionBehavior _interruptionBehavior = PlaylistInterruptionBehavior.Continue;
    private bool _advanceOnResume = false;
    private bool _stopAfterCurrentTrack;
    private readonly ModuleRole _moduleRole;
    private int _currentStatus = (int)PlaylistPlayerStatus.Stopped;

    /// -------------
    /// Hook para aplicar EQ/Trim/Norm antes de cada Play.
    /// Parametros: (canal, ruta del archivo, loguear detalles).
    /// Cuando logDetails es false, solo aplica la configuracion sin escribir
    /// en el log (evita duplicar entradas al reaplicar).
    /// -------------
    public Action<IAudioChannel, string, bool>? ConfigureChannel { get; set; }

    /// -------------
    /// Hook opcional para pre-calentar el analisis (silence + levels) de la
    /// proxima pista, mientras suena la actual. Esto reduce la race entre
    /// el analisis async y el Play: cuando la pista arranca, el analisis ya
    /// esta cacheado en memoria y el trim se aplica sincrono antes del Play.
    /// Se llama con el filePath de la proxima pista (o null si no hay).
    /// -------------
    public Action<string>? PrewarmFile { get; set; }

    // ───── Estado actual ─────────────────────────────────────────────────

    private Playlist? _playlist;
    private IAudioChannel? _currentChannel;
    private int _currentIndex = -1;

    /// -------------
    /// CTS de la locución en curso (si la pista actual es de tipo Locution).
    /// Se usa para cancelar la locución al pausar/detener/saltar.
    /// -------------
    private CancellationTokenSource? _currentLocutionCts;
    private CancellationTokenSource? _mixLocutionCts;

    /// -------------
    /// True si la pausa actual fue provocada por el scheduler (no por
    /// el usuario). Se usa para el indicador de pestañas.
    /// -------------
    private volatile bool _isPausedByCoordinator;

    /// -------------
    /// True si esta playlist esta pausada por el scheduler (interrumpida
    /// por un item ALTA) y todavia no fue reanudada.
    /// -------------
    public bool IsPausedByScheduler => _isPausedByCoordinator;

    /// -------------Cola de indices a reproducir (para shuffle). Vacia = orden secuencial.</summary>
    private readonly List<int> _shuffleQueue = new();

    /// -------------Indice actual dentro de la cola de shuffle (o -1 si no hay shuffle).</summary>
    private int _shufflePosition = -1;

    /// -------------Posicion guardada al pausar.</summary>
    private double _pausedPositionSeconds = 0;
    private PlaylistTrack? _currentTrack;

    // ───── Estado del mixado ─────────────────────────────────────────────
    private IAudioChannel? _mixChannel;
    private System.Threading.Timer? _mixTimer;
    private double _mixOriginalNormGainDb;
    private IAudioChannel? _encadenadorChannel;
    private readonly HashSet<IAudioChannel> _encadenadorTails = new();
    private int _encadenadorNextIndex = -1;
    private bool _encadenadorNextStarted;

    // ───── Timer de progreso ─────────────────────────────────────────────

    private System.Threading.Timer? _progressTimer;

    // ───── Eventos ───────────────────────────────────────────────────────

    public event EventHandler<PlaylistPlayerState>? StateChanged;
    /// -------------Se dispara cuando una pista termina naturalmente (no por Stop/Next).</summary>
    public event EventHandler<int>? TrackEnded;
    /// -------------Se dispara cuando una pista termino naturalmente.</summary>
    public event EventHandler<int>? TrackCompleted;
    /// -------------Se dispara cuando arranca una sesion nueva de reproduccion (Play).</summary>
    public event EventHandler? SessionStarted;
    /// -------------Se dispara cuando se detiene la reproduccion (Stop).</summary>
    public event EventHandler? SessionEnded;

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaylistPlayer(
        IAudioEngine audioEngine,
        Action<Action> dispatch,
        LocutionService? locution = null,
        BusExclusivityCoordinator? coordinator = null,
        EncadenadoresRegistry? encadenadores = null,
        ModuleRole moduleRole = ModuleRole.Playlist)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _locution = locution;
        _coordinator = coordinator;
        _encadenadores = encadenadores;
        _moduleRole = moduleRole;

        if (_coordinator is not null)
        {
            _coordinator.ChannelExpropriated += OnChannelExpropriated;
            _coordinator.ChannelResumed += OnChannelResumed;
        }
    }

    // ───── Configuracion publica ─────────────────────────────────────────

    public void SetBus(IAudioBus? bus)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_targetBus, bus)) return;
            var previousBus = _targetBus?.DisplayName ?? "(sin asignar)";
            _targetBus = bus;
            AppLogger.Info("PLAYLIST ROUTING",
                $"Bus cambiado: {previousBus} -> {bus?.DisplayName ?? "(sin asignar)"}.");

            if (!string.IsNullOrEmpty(_acquiredBusId) &&
                (bus is null || !string.Equals(bus.Id, _acquiredBusId, StringComparison.OrdinalIgnoreCase)))
            {
                ReleaseBusInternal();
            }
        }
    }

    /// -------------
    /// Bus de salida actual de esta playlist, o null si aun no se asigno.
    /// Se expone para que el MainWindowViewModel pueda consultarlo sin
    /// necesitar reflection sobre el campo privado _targetBus.
    /// -------------
    public IAudioBus? TargetBus
    {
        get { lock (_gate) return _targetBus; }
    }

    /// -------------
    /// Configura la duracion del crossfade entre pistas. 0 = transicion
    /// seca (sin solapamiento). Se debe llamar al arranque y cada vez que
    /// el usuario cambia el valor en la configuracion.
    /// -------------
    public void SetCrossfadeSeconds(double seconds)
    {
        lock (_gate) _crossfadeSeconds = Math.Max(0, seconds);
    }

    /// -------------Establece el comportamiento al ser interrumpido por un programado.</summary>
    public void SetInterruptionBehavior(PlaylistInterruptionBehavior behavior)
    {
        lock (_gate) _interruptionBehavior = behavior;
    }

    /// -------------Ruta del archivo que esta reproduciendo ahora (o vacio).</summary>
    public string CurrentFilePath { get; private set; } = string.Empty;

    public bool IsEncadenadorPlaying
    {
        get { lock (_gate) return _encadenadorChannel is not null; }
    }

    public (string Title, string FilePath)? GetUpcomingEncadenadorInfo()
    {
        lock (_gate)
        {
            if (_encadenadorChannel is not null || _playlist is null || _currentChannel is null)
                return null;
            var next = PeekNextIndex();
            if (next < 0 || next >= _playlist.Tracks.Count) return null;
            var track = _playlist.Tracks[next];
            if (!track.IsEnabled || track.ItemKind != PlaylistItemKind.Audio ||
                string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return null;
            if (_encadenadores is null ||
                !_encadenadores.TryPeekAfterAudio($"playlist:{_playlist.Id}", out var chain) ||
                chain is null)
                return null;
            return (chain.DisplayTitle, chain.FilePath);
        }
    }

    /// -------------
    /// Reaplica la configuracion al canal actual (EQ / Trim / Normalizacion).
    /// Se usa desde la UI cuando termina un analisis en background para el
    /// archivo que esta sonando.
    /// -------------
    public void ReapplyCurrentChannelConfig()
    {
        if (IsEncadenadorPlaying) return;
        var ch = _currentChannel;
        var path = CurrentFilePath;

        if (ch is null) return;
        if (string.IsNullOrWhiteSpace(path)) return;

        try { ConfigureChannel?.Invoke(ch, path, false); }
        catch (Exception ex) { AppLogger.Error("PLAYLIST CONFIG", $"No se pudo reaplicar procesamiento a '{Path.GetFileName(path)}'", ex); }
    }

    /// -------------Canal actualmente sonando (o null).</summary>
    public IAudioChannel? CurrentChannel => _currentChannel;

    /// -------------Último estado publicado por este reproductor.</summary>
    public PlaylistPlayerStatus CurrentStatus =>
        (PlaylistPlayerStatus)Volatile.Read(ref _currentStatus);

    /// -------------
    /// Devuelve todos los canales activos del PlaylistPlayer: la pista
    /// principal actual y el mixado si esta sonando.
    /// Se usa para propagar cambios de EQ/Trim/Norm a todos los canales vivos.
    /// -------------
    public IEnumerable<IAudioChannel> GetActiveChannels()
    {
        var list = new List<IAudioChannel>();

        lock (_gate)
        {
            if (_currentChannel is not null && !ReferenceEquals(_currentChannel, _encadenadorChannel))
                list.Add(_currentChannel);

            if (_mixChannel is not null)
                list.Add(_mixChannel);
        }

        return list;
    }

    /// -------------
    /// True si hay una pista reproduciendose AHORA (no pausada, no detenida).
    /// -------------
    public bool IsPlayingNow
    {
        get
        {
            lock (_gate)
            {
                if (_playlist is null) return false;

                // Locución en curso.
                if (_currentLocutionCts is not null) return true;

                if (_currentChannel is null) return false;
                try
                {
                    return _currentChannel.State == Abstractions.PlaybackState.Playing;
                }
                catch { return false; }
            }
        }
    }

    /// -------------Playlist que esta sonando (o null).</summary>
    public Playlist? CurrentPlaylist => _playlist;

    /// -------------Indice de la pista actual (0-based) o -1 si no hay.</summary>
    public int CurrentIndex => _currentIndex;

    // ───── API publica ───────────────────────────────────────────────────

    /// -------------
    /// Empieza a reproducir una playlist desde el indice indicado.
    /// Si ya habia una playlist sonando, se detiene.
    /// -------------
    public void Play(Playlist playlist, int startIndex = 0)
    {
        if (playlist is null) return;

        lock (_gate)
        {
            _isPausedByCoordinator = false;
            _isCrossfading = false;
            _stopAfterCurrentTrack = false;
            CancelCurrentLocution();
            var enabledIndexes = GetEnabledIndexes(playlist);
            if (enabledIndexes.Count == 0)
            {
                RaiseState(new PlaylistPlayerState(
                    PlaylistPlayerStatus.Error,
                    playlist.Name, -1, 0,
                    string.Empty, 0, 0,
                    "La playlist no tiene pistas habilitadas"));
                return;
            }

            // Normalizar el indice al primer elemento valido si esta deshabilitado.
            if (startIndex < 0 || startIndex >= playlist.Tracks.Count || !playlist.Tracks[startIndex].IsEnabled)
                startIndex = enabledIndexes[0];

            _playlist = playlist;
            _shuffleQueue.Clear();
            _shufflePosition = -1;
            _pausedPositionSeconds = 0;
            // Resetear flags de reproducido en el modelo.

            SessionStarted?.Invoke(this, EventArgs.Empty);

            if (playlist.Mode == PlaylistMode.Shuffle)
                BuildShuffleQueue(playlist, startIndex);

            PlayTrackInternal(startIndex);
        }
    }

    /// <summary>Solicita terminar la pista audible actual y detener la playlist sin avanzar.</summary>
    public bool StopAfterCurrentTrack()
    {
        lock (_gate)
        {
            if (_playlist is null || (_currentChannel is null && _currentLocutionCts is null)) return false;
            if (_stopAfterCurrentTrack) return true;

            _stopAfterCurrentTrack = true;
            if (_currentChannel is null)
            {
                AppLogger.Info("AUTODJ", $"La playlist '{_playlist.Name}' cederá el aire al terminar la locución actual.");
                return true;
            }
            try { _currentChannel.NearEnd -= OnChannelNearEnd; } catch { }
            try { _currentChannel.SetCrossfade(0); } catch { }

            // Si suena un encadenador, que complete el corte actual sin lanzar
            // antes la siguiente pista de la playlist.
            if (_encadenadorChannel is not null)
            {
                try { _encadenadorChannel.NearEnd -= OnEncadenadorNearEnd; } catch { }
                try { _encadenadorChannel.SetCrossfade(0); } catch { }
            }

            AppLogger.Info("AUTODJ", $"La playlist '{_playlist.Name}' terminará la pista actual y cederá el aire.");
            return true;
        }
    }

    /// <summary>Cancela una cesión pendiente solicitada por AutoDJ.</summary>
    public void CancelStopAfterCurrentTrack()
    {
        lock (_gate)
        {
            if (!_stopAfterCurrentTrack) return;
            _stopAfterCurrentTrack = false;
            var channel = _encadenadorChannel ?? _currentChannel;
            if (channel is null || _crossfadeSeconds <= 0.05) return;
            try
            {
                channel.SetCrossfade(_crossfadeSeconds);
                if (ReferenceEquals(channel, _encadenadorChannel))
                    channel.NearEnd += OnEncadenadorNearEnd;
                else
                    channel.NearEnd += OnChannelNearEnd;
            }
            catch { }
        }
    }

    /// -------------Pausa la reproduccion actual (puede reanudarse).</summary>
    public void Pause()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_playlist is null) return;
                AppLogger.Info("PLAYLIST CONTROL", $"Pausa solicitada: '{_playlist.Name}'.");

                // ===== FIX: pausa MANUAL =====
                // Marcar como NO-pausada-por-coordinator. Sin esto, si la
                // playlist habia sido pausada antes por el scheduler y luego
                // el usuario la pausa manualmente, el flag quedaba en true y
                // ResumePlaylistsPausedByScheduler la reanudaba sin permiso.
                _isPausedByCoordinator = false;

                // Si es una locución en curso: cancelarla (no se puede
                // pausar, es una reproducción externa).
                if (_currentLocutionCts is not null)
                {
                    CancelCurrentLocution();
                    // ===== FIX: detener el timer de progreso =====
                    StopProgressTimer();
                    RaiseState(BuildState(PlaylistPlayerStatus.Paused));
                    return;
                }

                if (_currentChannel is null) return;

                // El mix de locución corre independientemente del canal de música.
                // Al pausar manualmente también se debe detener.
                CancelMixLocution();

                try
                {
                    _pausedPositionSeconds = _currentChannel.Position.TotalSeconds;
                    _currentChannel.Pause();
                }
                catch { }

                foreach (var tail in _encadenadorTails)
                    try { tail.Pause(); } catch { }

                // Liberar el bus para que otro módulo pueda tomarlo.
                ReleaseBusInternal();

                // ===== FIX: detener el timer de progreso =====
                // Sin esto, EmitProgressTick seguia emitiendo Playing cada
                // 200ms, pisando el estado Paused y dejando la UI (y los
                // botones) en estado "reproduciendo" indefinidamente.
                StopProgressTimer();

                RaiseState(BuildState(PlaylistPlayerStatus.Paused));
            }
        });
    }

    /// -------------Reanuda desde la posicion guardada.</summary>
    public void Resume()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                _isPausedByCoordinator = false;
                if (_currentChannel is null) return;
                if (_playlist is null) return;
                AppLogger.Info("PLAYLIST CONTROL", $"Reanudación solicitada: '{_playlist.Name}'.");

                var bus = _targetBus ?? _audioEngine.PrimaryBus;
                var acquire = TryAcquireBus(bus.Id, _currentChannel);
                if (acquire.Denied)
                {
                    RaiseState(BuildState(PlaylistPlayerStatus.Paused));
                    return;
                }

                try { _currentChannel.Play(); }
                catch { }

                foreach (var tail in _encadenadorTails)
                    try { tail.Play(); } catch { }

                RaiseState(BuildState(PlaylistPlayerStatus.Playing));
                StartProgressTimer();
            }
        });
    }

    /// -------------Detiene la reproduccion completamente.</summary>
    public void Stop()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_playlist is not null)
                    AppLogger.Info("PLAYLIST CONTROL", $"Detener solicitado: '{_playlist.Name}'.");
                CancelCurrentLocution();
                StopCurrentChannelInternal();
                _playlist = null;
                _currentIndex = -1;
                _shuffleQueue.Clear();
                _shufflePosition = -1;
                _pausedPositionSeconds = 0;
                _advanceOnResume = false;
                SessionEnded?.Invoke(this, EventArgs.Empty);
                RaiseState(PlaylistPlayerState.Stopped);
            }
        });
    }

    /// -------------Salta a la pista siguiente (o se detiene si era la ultima en modo Once).</summary>
    public void Next()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_playlist is null) return;
                AppLogger.Info("PLAYLIST CONTROL", $"Siguiente solicitado: '{_playlist.Name}'.");
                CancelCurrentLocution();
                AdvanceToNext(auto: false);
            }
        });
    }

    /// -------------Salta a la pista anterior.</summary>
    public void Prev()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_playlist is null) return;
                AppLogger.Info("PLAYLIST CONTROL", $"Anterior solicitado: '{_playlist.Name}'.");
                CancelCurrentLocution();
                GoToPrevious();
            }
        });
    }

    /// -------------True si hay una playlist sonando o pausada.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate) return _playlist is not null;
        }
    }

    // ───── Reproduccion de pistas ────────────────────────────────────────

    private void PlayTrackInternal(int index, bool preserveEncadenadores = false)
    {
        if (_playlist is null) return;

        if (index < 0 || index >= _playlist.Tracks.Count)
        {
            Stop();
            return;
        }

        var track = _playlist.Tracks[index];

        if (!track.IsEnabled)
        {
            // Pista deshabilitada: saltar.
            int next = GetNextIndexFrom(index);
            if (next < 0 || next == index) { Stop(); return; }
            PlayTrackInternal(next);
            return;
        }

        // ── Locución externa (comando de RMVoice) ───────────────────────
        if (track.ItemKind == PlaylistItemKind.Locution)
        {
            PlayLocutionTrack(index, track);
            return;
        }

        // ── Audio (comportamiento clásico) ──────────────────────────────
        if (string.IsNullOrWhiteSpace(track.FilePath))
        {
            AppLogger.Warning("PLAYLIST",
                $"Playlist '{_playlist?.Name ?? "?"}': pista {index + 1} sin archivo asignado. Se saltea.");

            int next = GetNextIndexFrom(index);
            if (next < 0 || next == index) { Stop(); return; }
            PlayTrackInternal(next);
            return;
        }

        if (!File.Exists(track.FilePath))
        {
            AppLogger.Warning("PLAYLIST",
                $"Playlist '{_playlist?.Name ?? "?"}': archivo no encontrado en pista {index + 1}: {track.FilePath}. Se saltea.");

            int next = GetNextIndexFrom(index);
            if (next < 0 || next == index) { Stop(); return; }
            PlayTrackInternal(next);
            return;
        }

        StopProgressTimer();
        StopMixTimer();
        StopMixChannel();
        StopCurrentChannelInternal(keepPlaylist: true, preserveEncadenadores: preserveEncadenadores);

        var bus = _targetBus ?? _audioEngine.PrimaryBus;

        try
        {
            var channel = bus.LoadFile(track.FilePath);

            // ===== EXCLUSIVIDAD DE BUS =====
            var acquire = TryAcquireBus(bus.Id, channel);
            if (acquire.Denied)
            {
                AppLogger.Warning("PLAYLIST",
                    $"Pista rechazada: bus '{bus.DisplayName}' ocupado por mayor prioridad.");
                try { channel.Dispose(); } catch { }
                RaiseState(PlaylistPlayerState.Stopped);
                return;
            }

            _currentChannel = channel;
            _currentIndex = index;
            _currentTrack = track;
            CurrentFilePath = track.FilePath;

            channel.Ended += OnChannelEnded;

            var deviceName = bus.DeviceDisplayName;
            var fileName = Path.GetFileName(track.FilePath);
            int trackNum = index + 1;
            int trackTotal = _playlist?.Tracks.Count ?? 0;

            AppLogger.Info("PLAYLIST PLAY",
                $"Device: {deviceName} | " +
                $"Playlist: {_playlist?.Name ?? "?"} | " +
                $"Pista {trackNum}/{trackTotal} | " +
                $"Reproduciendo: {fileName}" +
                (track.HasMix ? $" | MIX: {track.MixDisplayName}" : ""));

            try { ConfigureChannel?.Invoke(channel, track.FilePath, true); }
            catch (Exception ex) { AppLogger.Error("PLAYLIST CONFIG", $"No se pudo configurar '{fileName}'", ex); }

            // ===== CROSSFADE: activar el near-end trigger =====
            if (_crossfadeSeconds > 0.05)
            {
                channel.SetCrossfade(_crossfadeSeconds);
                channel.NearEnd += OnChannelNearEnd;
            }

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

            if (acquire.ExpropriatedOwnerId is not null && _coordinator is not null)
            {
                // Expropiamos a otro modulo: fade-in suave.
                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);
                channel.FadeIn(ms, 1.0);
            }
            else if (_crossfadeSeconds > 0.05)
            {
                // Crossfade activo: fade-in para la primera pista (o
                // cuando el anterior termino de forma seca).
                int fadeMs = (int)(_crossfadeSeconds * 1000);
                channel.FadeIn(fadeMs, 1.0);
            }
            else
            {
                channel.Play();
            }

            // Precalentar el analisis de la proxima pista MIENTRAS suena la
            // actual. Cuando arranque, el analisis ya estara cacheado y el
            // trim se aplicara sincrono antes de Play(), evitando la race
            // de cache miss.
            try
            {
                var playlist = _playlist;
                if (playlist is not null && index + 1 < playlist.Tracks.Count)
                {
                    var nextTrack = playlist.Tracks[index + 1];
                    if (!string.IsNullOrWhiteSpace(nextTrack.FilePath) &&
                        File.Exists(nextTrack.FilePath))
                    {
                        PrewarmFile?.Invoke(nextTrack.FilePath);
                    }
                }
            }
            catch { }

            if (track.HasMix && track.MixContentType == MixContentType.Audio &&
                !string.IsNullOrWhiteSpace(track.MixAudioPath) &&
                File.Exists(track.MixAudioPath))
            {
                StartMixTimer(track);
            }
            else if (track.HasMix && track.MixContentType == MixContentType.Locution)
            {
                StartMixTimer(track);
            }

            RaiseState(BuildState(PlaylistPlayerStatus.Playing));
            StartProgressTimer();
        }
        catch (Exception ex)
        {
            _currentChannel = null;
            _currentIndex = -1;

            AppLogger.Error("PLAYLIST",
                $"Error reproduciendo pista {index + 1} de '{_playlist?.Name ?? "?"}': {ex.Message}", ex);

            RaiseState(new PlaylistPlayerState(
                PlaylistPlayerStatus.Error,
                _playlist?.Name ?? "?",
                -1, _playlist?.Tracks.Count ?? 0,
                string.Empty, 0, 0,
                ex.Message));
        }
    }

    /// -------------
    /// Reproduce una pista de tipo Locución. No carga canal de audio; en
    /// su lugar dispara el comando via LocutionService y avanza a la
    /// siguiente pista cuando termina.
    /// -------------
    private void PlayLocutionTrack(int index, PlaylistTrack track)
    {
        StopProgressTimer();
        StopMixTimer();
        StopMixChannel();
        StopCurrentChannelInternal(keepPlaylist: true);
        CancelCurrentLocution();

        _currentChannel = null;
        _currentIndex = index;
        _currentTrack = track;
        CurrentFilePath = string.Empty;

        var bus = _targetBus ?? _audioEngine.PrimaryBus;
        int deviceIndex = 0;
        try { deviceIndex = bus.ResolvedDeviceIndex; } catch { }

        // Ajuste de volumen relativo de la pista (dB → factor lineal).
        double volumeLinear = Math.Pow(10.0, track.VolumeDb / 20.0);
        float volume = (float)Math.Clamp(volumeLinear, 0.0, 1.0);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _currentLocutionCts = cts;

        AppLogger.Info("PLAYLIST PLAY",
            $"Device: {bus.DeviceDisplayName} | " +
            $"Playlist: {_playlist?.Name ?? "?"} | " +
            $"Pista {index + 1}/{_playlist?.Tracks.Count ?? 0} | " +
            $"LOCUCIÓN: {track.DisplayTitle}");

        RaiseState(BuildState(PlaylistPlayerStatus.Playing));
        StartProgressTimer();

        _ = RunLocutionAndAdvanceAsync(index, track, deviceIndex, volume, cts);
    }

    /// -------------
    /// Ejecuta la locución y, al terminar, avanza a la siguiente pista.
    /// -------------
    private async Task RunLocutionAndAdvanceAsync(
        int index,
        PlaylistTrack track,
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
                // Sin RMVoice: esperar un margen mínimo para que el flujo
                // no se corte y el usuario vea el intento.
                AppLogger.Warning("PLAYLIST",
                    "Locución solicitada pero RMVoice no está disponible. Se saltea.");
                await Task.Delay(500, cts.Token);
            }

            if (cts.IsCancellationRequested) return;

            _dispatch(() =>
            {
                lock (_gate)
                {
                    if (_currentIndex != index) return;
                    _currentLocutionCts = null;
                    if (_stopAfterCurrentTrack)
                    {
                        StopAfterTrackBoundaryInternal();
                        return;
                    }
                    AdvanceToNext(auto: true);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelado por Stop/Pause/Next: no avanzar.
        }
        catch (Exception ex)
        {
            AppLogger.Error("PLAYLIST",
                $"Error reproduciendo locución '{track.DisplayTitle}': {ex.Message}", ex);

            // Igual avanzar para no dejar la playlist trabada.
            _dispatch(() =>
            {
                lock (_gate)
                {
                    if (_currentIndex != index) return;
                    _currentLocutionCts = null;
                    if (_stopAfterCurrentTrack)
                    {
                        StopAfterTrackBoundaryInternal();
                        return;
                    }
                    AdvanceToNext(auto: true);
                }
            });
        }
        finally
        {
            if (ReferenceEquals(_currentLocutionCts, cts))
                _currentLocutionCts = null;
        }
    }

    /// -------------
    /// Cancela la locución en curso (si hay). No dispara avance.
    /// -------------
    private void CancelCurrentLocution()
    {
        var cts = _currentLocutionCts;
        if (cts is null) return;

        _currentLocutionCts = null;

        try { cts.Cancel(); } catch { }
        try { _locution?.Stop(); } catch { }
        try { cts.Dispose(); } catch { }
    }

    private void CancelMixLocution()
    {
        var cts = _mixLocutionCts;
        if (cts is null) return;

        _mixLocutionCts = null;
        try { cts.Cancel(); } catch { }
        try { _locution?.Stop(); } catch { }
    }

    // ───── Avance entre pistas ───────────────────────────────────────────

    private void OnChannelNearEnd(object? sender, EventArgs e)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_stopAfterCurrentTrack) return;
                if (_playlist is null) return;
                if (!ReferenceEquals(sender, _currentChannel)) return;
                if (_isCrossfading) return;

                int currentIndex = _currentIndex;
                int nextIndex = GetNextIndex();

                if (nextIndex < 0)
                {
                    // Sin siguiente: dejar el Ended natural manejar el fin.
                    return;
                }

                var nextTrack = _playlist.Tracks[nextIndex];
                if (!nextTrack.IsEnabled) return;
                if (string.IsNullOrWhiteSpace(nextTrack.FilePath)) return;
                if (!File.Exists(nextTrack.FilePath)) return;

                var currentCh = _currentChannel;
                if (currentCh is null) return;

                if (TryStartEncadenador(nextIndex, currentCh))
                    return;

                _isCrossfading = true;

                try
                {
                    var bus = _targetBus ?? _audioEngine.PrimaryBus;
                    var nextCh = bus.LoadFile(nextTrack.FilePath);

                    try { ConfigureChannel?.Invoke(nextCh, nextTrack.FilePath, true); } catch { }

                    // Aplicar volumen del track.
                    double trackGainDb = nextTrack.VolumeDb;
                    if (Math.Abs(trackGainDb) > 0.01)
                    {
                        try
                        {
                            double currentNorm = nextCh.NormalizationGainDb;
                            nextCh.SetNormalizationGain(currentNorm + trackGainDb);
                        }
                        catch { }
                    }

                    // Configurar crossfade del siguiente.
                    if (_crossfadeSeconds > 0.05)
                    {
                        nextCh.SetCrossfade(_crossfadeSeconds);
                        nextCh.NearEnd += OnChannelNearEnd;
                    }

                    nextCh.Ended += OnChannelEnded;

                    // Actualizar estado ANTES de arrancar el siguiente.
                    _currentChannel = nextCh;
                    _currentIndex = nextIndex;
                    _currentTrack = nextTrack;
                    CurrentFilePath = nextTrack.FilePath;

                    int fadeMs = (int)(_crossfadeSeconds * 1000);

                    // FadeIn del siguiente y FadeOut del actual en paralelo.
                    nextCh.FadeIn(fadeMs, 1.0);
                    currentCh.FadeOut(fadeMs);

                    // Programar dispose del canal viejo.
                    _ = DisposeOldChannelAsync(currentCh, fadeMs);

                    // Cancelar el mix timer pendiente y hacer fade-out del
                    // mix activo si lo hay (se solapa con el crossfade).
                    StopMixTimer();
                    if (_mixChannel is not null)
                    {
                        var oldMix = _mixChannel;
                        _mixChannel = null;
                        try { oldMix.Ended -= OnMixChannelEnded; } catch { }
                        try { oldMix.FadeOut(fadeMs); } catch { try { oldMix.Stop(); } catch { } }
                        _ = Task.Delay(fadeMs + 100).ContinueWith(_ => { try { oldMix.Dispose(); } catch { } });
                    }
                    _mixOriginalNormGainDb = 0;

                    // Marcar la anterior como completada en la UI.
                    TrackEnded?.Invoke(this, currentIndex);
                    TrackCompleted?.Invoke(this, currentIndex);

                    AppLogger.Info("PLAYLIST PLAY",
                        $"CROSSFADE ({fadeMs}ms): '{Path.GetFileName(currentCh.FilePath)}' -> '{Path.GetFileName(nextTrack.FilePath)}' " +
                        $"[{nextIndex + 1}/{_playlist.Tracks.Count}]");

                    // Mix del siguiente (si aplica).
                    if (nextTrack.HasMix && nextTrack.MixContentType == MixContentType.Audio &&
                        !string.IsNullOrWhiteSpace(nextTrack.MixAudioPath) &&
                        File.Exists(nextTrack.MixAudioPath))
                    {
                        StartMixTimer(nextTrack);
                    }
                    else if (nextTrack.HasMix && nextTrack.MixContentType == MixContentType.Locution)
                    {
                        StartMixTimer(nextTrack);
                    }

                    RaiseState(BuildState(PlaylistPlayerStatus.Playing));

                    // Reset del flag tras el fade.
                    _ = Task.Delay(fadeMs + 100).ContinueWith(_ =>
                    {
                        lock (_gate) _isCrossfading = false;
                    });
                }
                catch (Exception ex)
                {
                    _isCrossfading = false;
                    AppLogger.Error("PLAYLIST", $"Error en crossfade: {ex.Message}", ex);
                    // Fallback: el Ended natural del canal actual va a avanzar.
                }
            }
        });
    }

    private bool TryStartEncadenador(int nextIndex, IAudioChannel? oldChannel)
    {
        if (_encadenadores is null || _playlist is null || _encadenadorChannel is not null)
            return false;
        if (nextIndex < 0 || nextIndex >= _playlist.Tracks.Count) return false;

        var nextTrack = _playlist.Tracks[nextIndex];
        if (!nextTrack.IsEnabled || nextTrack.ItemKind != PlaylistItemKind.Audio ||
            string.IsNullOrWhiteSpace(nextTrack.FilePath) || !File.Exists(nextTrack.FilePath))
            return false;

        if (!_encadenadores.TryTakeAfterAudio($"playlist:{_playlist.Id}", out var encadenador) || encadenador is null)
            return false;

        IAudioChannel chainChannel;
        try
        {
            var bus = _targetBus ?? _audioEngine.PrimaryBus;
            chainChannel = bus.LoadFile(encadenador.FilePath);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("ENCADENADORES", $"No se pudo cargar '{encadenador.FilePath}': {ex.Message}");
            return false;
        }

        var fadeMs = (int)Math.Max(0, _crossfadeSeconds * 1000);
        _encadenadorChannel = chainChannel;
        _encadenadorNextIndex = nextIndex;
        _encadenadorNextStarted = false;
        _currentChannel = chainChannel;
        CurrentFilePath = encadenador.FilePath;
        chainChannel.Ended += OnEncadenadorChannelEnded;

        if (_crossfadeSeconds > 0.05 && chainChannel.Duration.TotalSeconds > _crossfadeSeconds)
        {
            chainChannel.SetCrossfade(_crossfadeSeconds);
            chainChannel.NearEnd += OnEncadenadorNearEnd;
        }

        try
        {
            chainChannel.Play(); // audio en crudo: sin configurador, trim ni ganancia de pista
            if (oldChannel is not null)
            {
                try
                {
                    oldChannel.NearEnd -= OnChannelNearEnd;
                    oldChannel.Ended -= OnChannelEnded;
                    if (fadeMs > 0) oldChannel.FadeOut(fadeMs);
                    else oldChannel.Stop();
                    _ = DisposeOldChannelAsync(oldChannel, fadeMs);
                }
                catch { try { oldChannel.Dispose(); } catch { } }
            }
            RaiseState(BuildState(PlaylistPlayerStatus.Playing));
            StartProgressTimer();
            AppLogger.Info("ENCADENADORES",
                $"Reproduciendo '{encadenador.DisplayTitle}' entre pistas de '{_playlist.Name}'.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("ENCADENADORES", $"Error reproduciendo '{encadenador.FilePath}': {ex.Message}");
            StopEncadenadorChannels();
            return false;
        }
    }

    private void OnEncadenadorNearEnd(object? sender, EventArgs e)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(sender, _encadenadorChannel) || _encadenadorNextStarted)
                    return;
                if (_stopAfterCurrentTrack) return;
                StartNextTrackDuringEncadenador();
            }
        });
    }

    private void OnEncadenadorChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (sender is not IAudioChannel channel) return;
                if (!ReferenceEquals(channel, _encadenadorChannel))
                {
                    _encadenadorTails.Remove(channel);
                    try { channel.Ended -= OnEncadenadorChannelEnded; channel.Dispose(); } catch { }
                    return;
                }

                channel.Ended -= OnEncadenadorChannelEnded;
                channel.NearEnd -= OnEncadenadorNearEnd;
                _encadenadorChannel = null;
                if (ReferenceEquals(_currentChannel, channel)) _currentChannel = null;
                try { channel.Dispose(); } catch { }

                if (!_encadenadorNextStarted)
                {
                    if (_stopAfterCurrentTrack)
                    {
                        StopAfterTrackBoundaryInternal();
                        return;
                    }
                    StartNextTrackDuringEncadenador();
                }
            }
        });
    }

    private void StartNextTrackDuringEncadenador()
    {
        if (_encadenadorNextStarted) return;
        _encadenadorNextStarted = true;
        var nextIndex = _encadenadorNextIndex;
        _encadenadorNextIndex = -1;

        if (nextIndex < 0 || _playlist is null)
        {
            Stop();
            return;
        }

        var chainChannel = _encadenadorChannel;
        if (chainChannel is not null)
        {
            chainChannel.NearEnd -= OnEncadenadorNearEnd;
            chainChannel.Ended -= OnEncadenadorChannelEnded;
            chainChannel.Ended += OnEncadenadorChannelEnded;
            _encadenadorTails.Add(chainChannel);
            _encadenadorChannel = null;
        }

        if (ReferenceEquals(_currentChannel, chainChannel)) _currentChannel = null;
        _isCrossfading = false;
        PlayTrackInternal(nextIndex, preserveEncadenadores: true);
    }

    private void StopEncadenadorChannels()
    {
        if (_encadenadorChannel is not null)
        {
            try
            {
                _encadenadorChannel.Ended -= OnEncadenadorChannelEnded;
                _encadenadorChannel.NearEnd -= OnEncadenadorNearEnd;
                _encadenadorChannel.Stop();
                _encadenadorChannel.Dispose();
            }
            catch { }
            _encadenadorChannel = null;
        }

        foreach (var channel in _encadenadorTails.ToArray())
        {
            try { channel.Ended -= OnEncadenadorChannelEnded; channel.Stop(); channel.Dispose(); } catch { }
        }
        _encadenadorTails.Clear();
        _encadenadorNextIndex = -1;
        _encadenadorNextStarted = false;
    }

    private async Task DisposeOldChannelAsync(IAudioChannel oldChannel, int fadeMs)
    {
        try
        {
            await Task.Delay(fadeMs + 100).ConfigureAwait(false);
            _dispatch(() =>
            {
                try
                {
                    oldChannel.Ended -= OnChannelEnded;
                    oldChannel.NearEnd -= OnChannelNearEnd;
                    oldChannel.Dispose();
                }
                catch { }
            });
        }
        catch { }
    }

    private void OnChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_currentChannel is null) return;
                if (!ReferenceEquals(sender, _currentChannel)) return; // ignorar canales viejos

                var endedIndex = _currentIndex;
                var endedTitle = (endedIndex >= 0 && _playlist is not null && endedIndex < _playlist.Tracks.Count)
                    ? _playlist.Tracks[endedIndex].DisplayTitle
                    : "?";

                StopProgressTimer();

                try
                {
                    _currentChannel.Ended -= OnChannelEnded;
                    _currentChannel.Dispose();
                }
                catch { }

                _currentChannel = null;

                AppLogger.Info("PLAYLIST PLAY", $"Fin de pista: {endedTitle}");

                if (_playlist is null) return;

                TrackEnded?.Invoke(this, endedIndex);
                TrackCompleted?.Invoke(this, endedIndex);

                if (_stopAfterCurrentTrack)
                {
                    StopAfterTrackBoundaryInternal();
                    return;
                }

                var nextIndex = GetNextIndex();
                if (nextIndex >= 0 && TryStartEncadenador(nextIndex, oldChannel: null))
                    return;

                AdvanceToNext(auto: true);
            }
        });
    }

    /// -------------
    /// Avanza a la proxima pista.
    /// <paramref name="auto"/> = true si es por fin natural de la pista,
    /// false si es por Next() manual. En modo Loop, al final vuelve al inicio.
    /// En modo Once, al final se detiene (tanto para auto como manual).
    /// -------------
    private void AdvanceToNext(bool auto)
    {
        if (_playlist is null) return;

        int next = GetNextIndex();

        if (next < 0)
        {
            // No hay proxima pista.
            if (auto && _playlist.Mode == PlaylistMode.Loop)
            {
                // Volver al inicio.
                _shuffleQueue.Clear();
                _shufflePosition = -1;

                int first = GetFirstEnabledIndex(_playlist);
                if (first < 0) { Stop(); return; }

                if (_playlist.Mode == PlaylistMode.Shuffle)
                    BuildShuffleQueue(_playlist, first);

                PlayTrackInternal(first);
                return;
            }

            Stop();
            return;
        }

        // El salto manual mantiene la misma inserción de encadenadores que
        // el avance natural: si el siguiente elemento es un audio y el
        // contador indica que toca insertar, se reproduce el encadenador
        // antes de abrir ese audio.
        if (!auto && _currentTrack?.ItemKind == PlaylistItemKind.Audio &&
            next < _playlist.Tracks.Count &&
            _playlist.Tracks[next].ItemKind == PlaylistItemKind.Audio &&
            TryStartEncadenador(next, _currentChannel))
            return;

        PlayTrackInternal(next);
    }

    private void GoToPrevious()
    {
        if (_playlist is null) return;

        // Si estamos en medio de una pista (>3s), volver al inicio de la misma.
        try
        {
            if (_currentChannel is not null &&
                _currentChannel.Position.TotalSeconds > 3.0)
            {
                _currentChannel.Seek(TimeSpan.Zero);
                return;
            }
        }
        catch { }

        int prev = GetPreviousIndex();
        if (prev < 0)
        {
            // Ya estamos en la primera.
            try { _currentChannel?.Seek(TimeSpan.Zero); } catch { }
            return;
        }

        PlayTrackInternal(prev);
    }

    // ───── Calculo de indices ────────────────────────────────────────────

    private int GetNextIndex()
    {
        if (_playlist is null) return -1;

        if (_playlist.Mode == PlaylistMode.Shuffle && _shuffleQueue.Count > 0)
        {
            int newPos = _shufflePosition + 1;
            if (newPos >= _shuffleQueue.Count) return -1;
            _shufflePosition = newPos;
            return _shuffleQueue[newPos];
        }

        // Modo secuencial (Once o Loop).
        for (int i = _currentIndex + 1; i < _playlist.Tracks.Count; i++)
        {
            if (_playlist.Tracks[i].IsEnabled)
                return i;
        }

        return -1;
    }

    /// -------------
    /// Version "peek" de GetNextIndex: devuelve el indice de la proxima
    /// pista SIN modificar el estado de reproduccion (no avanza el shuffle).
    /// -------------
    private int PeekNextIndex()
    {
        if (_playlist is null) return -1;

        if (_playlist.Mode == PlaylistMode.Shuffle && _shuffleQueue.Count > 0)
        {
            int newPos = _shufflePosition + 1;
            if (newPos >= _shuffleQueue.Count) return -1;
            return _shuffleQueue[newPos];
        }

        for (int i = _currentIndex + 1; i < _playlist.Tracks.Count; i++)
        {
            if (_playlist.Tracks[i].IsEnabled)
                return i;
        }

        return -1;
    }

    /// -------------
    /// Info de la proxima pista a reproducir (sin reproducirla). Null si
    /// no hay siguiente (fin de la lista, modo Once sin Loop, o sin playlist).
    /// -------------
    public (int Index, int Total, string Title, string FilePath)? GetUpcomingTrackInfo()
    {
        lock (_gate)
        {
            if (_playlist is null) return null;
            if (_playlist.Tracks.Count == 0) return null;

            int next = PeekNextIndex();
            if (next < 0) return null;
            if (next >= _playlist.Tracks.Count) return null;

            var track = _playlist.Tracks[next];
            if (track is null) return null;

            return (next, _playlist.Tracks.Count, track.DisplayTitle, track.FilePath);
        }
    }

    /// -------------
    /// Calcula el indice de la proxima pista a partir de un indice explicitamente
    /// indicado, en lugar de _currentIndex. Se usa durante el salteo de pistas
    /// invalidas: en ese momento _currentIndex todavia apunta a la pista anterior
    /// porque la asignacion se hace recien despues de un LoadFile exitoso.
    ///
    /// Sin este helper, GetNextIndex() devolveria el mismo indice que estamos
    /// intentando reproducir y el chequeo "next == index" detendria la playlist
    /// entera en lugar de saltear la pista faltante.
    /// -------------
    private int GetNextIndexFrom(int currentAttempted)
    {
        if (_playlist is null) return -1;

        if (_playlist.Mode == PlaylistMode.Shuffle && _shuffleQueue.Count > 0)
        {
            int newPos = _shufflePosition + 1;
            if (newPos >= _shuffleQueue.Count) return -1;
            _shufflePosition = newPos;
            return _shuffleQueue[newPos];
        }

        for (int i = currentAttempted + 1; i < _playlist.Tracks.Count; i++)
        {
            if (_playlist.Tracks[i].IsEnabled)
                return i;
        }

        return -1;
    }

    private int GetPreviousIndex()
    {
        if (_playlist is null) return -1;

        if (_playlist.Mode == PlaylistMode.Shuffle && _shuffleQueue.Count > 0)
        {
            int newPos = _shufflePosition - 1;
            if (newPos < 0) return -1;
            _shufflePosition = newPos;
            return _shuffleQueue[newPos];
        }

        for (int i = _currentIndex - 1; i >= 0; i--)
        {
            if (_playlist.Tracks[i].IsEnabled)
                return i;
        }

        return -1;
    }

    private static List<int> GetEnabledIndexes(Playlist playlist)
    {
        var result = new List<int>();
        for (int i = 0; i < playlist.Tracks.Count; i++)
        {
            if (playlist.Tracks[i].IsEnabled)
                result.Add(i);
        }
        return result;
    }

    private static int GetFirstEnabledIndex(Playlist playlist)
    {
        for (int i = 0; i < playlist.Tracks.Count; i++)
        {
            if (playlist.Tracks[i].IsEnabled)
                return i;
        }
        return -1;
    }

    private void BuildShuffleQueue(Playlist playlist, int startingIndex)
    {
        _shuffleQueue.Clear();

        var rng = new Random();
        var indices = GetEnabledIndexes(playlist);

        // Colocar startingIndex primero.
        indices.Remove(startingIndex);
        indices = indices.OrderBy(_ => rng.Next()).ToList();

        _shuffleQueue.Add(startingIndex);
        _shuffleQueue.AddRange(indices);

        _shufflePosition = 0;
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void StopCurrentChannelInternal(bool keepPlaylist = false, bool preserveEncadenadores = false)
    {
        _stopAfterCurrentTrack = false;
        _isPausedByCoordinator = false;
        _advanceOnResume = false;

        CancelCurrentLocution();
        StopProgressTimer();
        StopMixTimer();
        CancelMixLocution();
        StopMixChannel();
        if (!preserveEncadenadores) StopEncadenadorChannels();
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

        _currentTrack = null;
        CurrentFilePath = string.Empty;

        ReleaseBusInternal();

        if (!keepPlaylist)
        {
            _playlist = null;
            _currentIndex = -1;
        }
    }

    private void StopAfterTrackBoundaryInternal()
    {
        if (!_stopAfterCurrentTrack) return;
        _stopAfterCurrentTrack = false;
        StopCurrentChannelInternal();
        _shuffleQueue.Clear();
        _shufflePosition = -1;
        _pausedPositionSeconds = 0;
        SessionEnded?.Invoke(this, EventArgs.Empty);
        RaiseState(PlaylistPlayerState.Stopped);
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

    // ───── Mixado ────────────────────────────────────────────────────────

    private void StartMixTimer(PlaylistTrack track)
    {
        StopMixTimer();

        double trigger = Math.Max(0, track.MixStartSeconds);

        if (trigger <= 0.05)
        {
            // Disparar inmediatamente (agendar en el dispatcher para no
            // bloquear el hilo actual).
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

    /// -------------
    /// Punto de entrada del mixado: deriva a audio o a locución según
    /// el tipo configurado en la pista.
    /// -------------
    private void StartMixPlayback()
    {
        var track = _currentTrack;
        var mainChannel = _currentChannel;

        if (track is null || mainChannel is null) return;
        if (!track.HasMix) return;

        if (track.MixContentType == MixContentType.Locution)
        {
            _ = StartLocutionMixAsync(track, mainChannel);
            return;
        }

        // ── Mix de audio (comportamiento clásico) ────────────────────────
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

            AppLogger.Info("PLAYLIST PLAY",
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

    /// -------------
    /// Ejecuta una locución de RMVoice como mixado intermedio.
    ///   - Baja el volumen de la pista principal al MixDuckLevel.
    ///   - Dispara la locución en el MISMO device del bus.
    ///   - Al terminar, restaura el volumen original.
    ///
    /// Si RMVoice no está disponible, no se hace nada (y se loguea un warning).
    /// Si la locución falla o tarda demasiado, se restaura el volumen igual.
    /// -------------
    private async Task StartLocutionMixAsync(PlaylistTrack track, IAudioChannel mainChannel)
    {
        if (_locution is null || !_locution.IsAvailable)
        {
            AppLogger.Warning("PLAYLIST PLAY",
                "Locución solicitada pero RMVoice no está disponible. " +
                "El audio principal continúa sin interrupción.");
            return;
        }

        // Device objetivo = device resuelto del bus de la playlist.
        int deviceIndex = ResolveCurrentBusDevice();

        // Guardar ganancia original + duck.
        var originalNormGainDb = mainChannel.NormalizationGainDb;
        _mixOriginalNormGainDb = originalNormGainDb;
        double duckDb = 20.0 * Math.Log10(Math.Max(0.001, track.MixDuckLevel));
        mainChannel.SetNormalizationGain(originalNormGainDb + duckDb);

        AppLogger.Info("PLAYLIST PLAY",
            $"LOCUCIÓN iniciada: {track.MixLocutionCommand} | " +
            $"Device: {deviceIndex} | " +
            $"Principal reducida a: {track.MixDuckLevel:P0}");

        CancellationTokenSource? cts = null;
        try
        {
            // ===== FIX: volumen de locución en mix =====
            // MixVolumeDb fue diseñado para jingles de fondo (default -12 dB =
            // ~25% del volumen original). Para una locución (voz hablada de
            // primer plano), ese nivel deja el audio prácticamente inaudible,
            // especialmente detrás de la música a MixDuckLevel (default 30%).
            //
            // Como la relación locución / música de fondo se controla con
            // MixDuckLevel (atenuación de la pista principal), la locución
            // puede ir a volumen pleno sin sonar "agresiva".
            //
            // Para audio mix (jingles/IDs) se sigue usando MixVolumeDb.
            float volumeLinear = track.MixContentType == MixContentType.Locution
                ? 1.0f
                : (float)Math.Pow(10.0, track.MixVolumeDb / 20.0);

            volumeLinear = Math.Clamp(volumeLinear, 0f, 1f);

            // Timeout de seguridad: 45s por si algo se cuelga.
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _mixLocutionCts = cts;
            await _locution.PlayAsync(track.MixLocutionCommand, deviceIndex, volumeLinear, cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warning("PLAYLIST PLAY",
                $"Locución {track.MixLocutionCommand} cancelada por timeout (45s).");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("PLAYLIST PLAY",
                $"Error en locución {track.MixLocutionCommand}: {ex.Message}");
        }
        finally
        {
            if (cts is not null && ReferenceEquals(_mixLocutionCts, cts))
                _mixLocutionCts = null;
            cts?.Dispose();
            // Restaurar volumen (best-effort; puede fallar si el canal ya se cerró).
            try
            {
                mainChannel.SetNormalizationGain(originalNormGainDb);
            }
            catch { }

            AppLogger.Info("PLAYLIST PLAY",
                $"LOCUCIÓN finalizada: {track.MixLocutionCommand}");
        }
    }

    /// -------------
    /// Devuelve el índice REAL de BASS del bus actual (resuelto de -1 a default).
    /// Es el device al que se manda la locución para que suene por el mismo
    /// canal que la playlist.
    /// -------------
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
        // Locución: no hay canal de mix que detener, RMVoice maneja el suyo.
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

    private void EmitProgressTick()
    {
        try
        {
            var ch = _currentChannel;
            var pl = _playlist;

            if (ch is null || pl is null) return;
            if (_currentIndex < 0) return;

            double pos = 0;
            double dur = 0;
            try
            {
                pos = ch.Position.TotalSeconds;
                dur = ch.Duration.TotalSeconds;
            }
            catch { return; }

            // ===== FIX: usar el estado real del canal =====
            // Antes se hardcodeaba Playing, pisando el estado Paused si el
            // timer seguia corriendo (aunque con el Fix 1 ya no corre durante
            // pausa, esto es defensa por si algun edge case lo deja vivo).
            var status = ch.State switch
            {
                Abstractions.PlaybackState.Paused => PlaylistPlayerStatus.Paused,
                Abstractions.PlaybackState.Playing => PlaylistPlayerStatus.Playing,
                Abstractions.PlaybackState.Stalled => PlaylistPlayerStatus.Playing,
                _ => PlaylistPlayerStatus.Stopped
            };

            if (status == PlaylistPlayerStatus.Stopped) return;

            RaiseState(new PlaylistPlayerState(
                status,
                pl.Name,
                _currentIndex,
                pl.Tracks.Count,
                pl.Tracks[_currentIndex].DisplayTitle,
                pos, dur, null));
        }
        catch { }
    }

    private PlaylistPlayerState BuildState(PlaylistPlayerStatus status)
    {
        if (_playlist is null)
            return PlaylistPlayerState.Stopped;

        double pos = _pausedPositionSeconds;
        double dur = 0;
        string title = string.Empty;

        if (_currentIndex >= 0 && _currentIndex < _playlist.Tracks.Count)
        {
            title = _playlist.Tracks[_currentIndex].DisplayTitle;
        }

        try
        {
            if (_currentChannel is not null)
            {
                pos = _currentChannel.Position.TotalSeconds;
                dur = _currentChannel.Duration.TotalSeconds;
            }
        }
        catch { }

        return new PlaylistPlayerState(
            status,
            _playlist.Name,
            _currentIndex,
            _playlist.Tracks.Count,
            title,
            pos, dur, null);
    }

    private void RaiseState(PlaylistPlayerState state)
    {
        Volatile.Write(ref _currentStatus, (int)state.Status);
        try { StateChanged?.Invoke(this, state); } catch { }
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_coordinator is not null)
        {
            _coordinator.ChannelExpropriated -= OnChannelExpropriated;
            _coordinator.ChannelResumed -= OnChannelResumed;
        }

        try
        {
            CancelCurrentLocution();
            StopMixTimer();
            StopMixChannel();
            StopCurrentChannelInternal();
            ReleaseBusInternal();
            try { _locution?.Stop(); } catch { }
        }
        catch { }
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

        var result = _coordinator.TryAcquire(busId, _instanceId, _moduleRole, channel);
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
        if (_coordinator is null) return;
        if (!string.Equals(e.OwnerId, _instanceId, StringComparison.Ordinal)) return;

        _dispatch(() =>
        {
            lock (_gate)
            {
                CancelCurrentLocution();
                CancelMixLocution();
                StopProgressTimer();
                StopMixTimer();
                StopMixChannel();
                _mixOriginalNormGainDb = 0;

                // Guardar posicion actual antes del fade-out.
                double savedPos = 0;
                if (_currentChannel is not null)
                {
                    try { savedPos = _currentChannel.Position.TotalSeconds; } catch { }
                }
                _pausedPositionSeconds = savedPos;

                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);

                // ===== RESOLVER EL EFECTO SEGUN e.Behavior =====
                // El SkipToNext del PlaylistInterruptionBehavior SOLO aplica
                // cuando el expropiador es AutoResume (típicamente el Scheduler).
                // Antes corria SIEMPRE, incluso cuando el expropiador era Stop
                // (Cart u otra Playlist), y la playlist quedaba colgada en
                // PausedByScheduler esperando un ChannelResumed que nunca llegaba.
                if (e.Behavior == InterruptionBehavior.AutoResume
                    && _interruptionBehavior == PlaylistInterruptionBehavior.SkipToNext)
                {
                    var ch = _currentChannel;
                    _currentChannel = null;
                    CurrentFilePath = string.Empty;
                    _acquiredBusId = string.Empty;

                    if (ch is not null)
                    {
                        try { ch.Ended -= OnChannelEnded; } catch { }
                        try { ch.FadeOut(ms, () => { try { ch.Dispose(); } catch { } }); }
                        catch { try { ch.Dispose(); } catch { } }
                    }

                    _advanceOnResume = true;
                    _isPausedByCoordinator = true;

                    AppLogger.Info("PLAYLIST",
                        $"Playlist expropiada del bus '{e.BusId}' por '{e.Role}' " +
                        $"(modo SkipToNext).");
                    RaiseState(BuildState(PlaylistPlayerStatus.PausedByScheduler));
                    return;
                }

                switch (e.Behavior)
                {
                    case InterruptionBehavior.Stop:
                        {
                            var ch = _currentChannel;
                            _currentChannel = null;
                            CurrentFilePath = string.Empty;
                            _currentTrack = null;
                            _playlist = null;
                            _currentIndex = -1;
                            _shuffleQueue.Clear();
                            _shufflePosition = -1;
                            _pausedPositionSeconds = 0;
                            _advanceOnResume = false;
                            _acquiredBusId = string.Empty;
                            _isPausedByCoordinator = false;

                            if (ch is not null)
                            {
                                try { ch.Ended -= OnChannelEnded; } catch { }
                                try { ch.FadeOut(ms, () => { try { ch.Dispose(); } catch { } }); }
                                catch { try { ch.Dispose(); } catch { } }
                            }

                            SessionEnded?.Invoke(this, EventArgs.Empty);
                            RaiseState(PlaylistPlayerState.Stopped);

                            AppLogger.Info("PLAYLIST",
                                $"Playlist expropiada del bus '{e.BusId}' por '{e.Role}' (Stop).");
                            break;
                        }

                    case InterruptionBehavior.Pause:
                    case InterruptionBehavior.AutoResume:
                        {
                            var ch = _currentChannel;
                            if (ch is not null)
                            {
                                try { ch.FadeOutAndPause(ms); }
                                catch { try { ch.Pause(); } catch { } }
                            }

                            _acquiredBusId = string.Empty;
                            _isPausedByCoordinator = true;

                            AppLogger.Info("PLAYLIST",
                                $"Playlist expropiada del bus '{e.BusId}' por '{e.Role}' ({e.Behavior}). " +
                                (e.Behavior == InterruptionBehavior.AutoResume
                                    ? "Se reanudara al liberar el bus."
                                    : "Espera reanudacion manual."));
                            RaiseState(BuildState(PlaylistPlayerStatus.PausedByScheduler));
                            break;
                        }
                }
            }
        });
    }

    private void OnChannelResumed(object? sender, ChannelResumedEventArgs e)
    {
        if (_coordinator is null) return;
        if (!string.Equals(e.OwnerId, _instanceId, StringComparison.Ordinal)) return;

        _dispatch(() =>
        {
            lock (_gate)
            {
                if (!_isPausedByCoordinator) return;
                _isPausedByCoordinator = false;

                // SkipToNext: avanzar al siguiente item en lugar de reanudar.
                if (_advanceOnResume)
                {
                    _advanceOnResume = false;
                    if (_playlist is null) return;

                    int next = GetNextIndex();
                    if (next < 0)
                    {
                        Stop();
                        return;
                    }
                    PlayTrackInternal(next);
                    return;
                }

                if (_playlist is null) return;

                // ===== FIX: LOCUCIONES INTERRUMPIDAS POR ALTA =====
                // Si la interrupción mató una locución, _currentChannel es
                // null (las locuciones no tienen canal de audio). No hay
                // "posición" que reanudar: la locución se canceló y se
                // perdió. Sin este branch, la playlist quedaba trabada
                // para siempre en PausedByScheduler (el _currentChannel
                // null hacía que el chequeo de abajo la ignorara).
                //
                // Solución: si el ítem actual era una locución, saltar a
                // la siguiente pista. Si el ítem era audio normal pero el
                // canal desapareció por algún error previo, dejamos el
                // return defensivo de abajo.
                if (_currentChannel is null)
                {
                    if (_currentIndex >= 0 &&
                        _currentIndex < _playlist.Tracks.Count &&
                        _playlist.Tracks[_currentIndex].ItemKind == PlaylistItemKind.Locution)
                    {
                        AppLogger.Info("PLAYLIST",
                            $"Locución interrumpida por '{e.Role}' finalizada. " +
                            "Saltando a la siguiente pista.");
                        AdvanceToNext(auto: true);
                    }
                    return;
                }

                // Reanudar canal pausado.
                var acquire = _coordinator.TryAcquire(
                    e.BusId, _instanceId, ModuleRole.Playlist, _currentChannel);
                if (acquire.Denied)
                {
                    RaiseState(BuildState(PlaylistPlayerStatus.Paused));
                    return;
                }
                _acquiredBusId = e.BusId;

                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);
                try
                {
                    // preservePosition: el canal esta en Paused en la posicion
                    // guardada; no queremos que se vaya al trimStart.
                    _currentChannel.FadeIn(ms, 1.0, preservePosition: true);
                }
                catch
                {
                    try { _currentChannel.Play(); } catch { }
                }

                RaiseState(BuildState(PlaylistPlayerStatus.Playing));
                StartProgressTimer();

                AppLogger.Info("PLAYLIST",
                    $"Playlist reanudada en bus '{e.BusId}' tras finalizar '{e.Role}'.");
            }
        });
    }
}
