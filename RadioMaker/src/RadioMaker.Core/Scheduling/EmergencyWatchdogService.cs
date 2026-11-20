using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Playback;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Watchdog de emergencia: detecta silencio prolongado en el bus de AIRE
/// y dispara automaticamente una playlist de emergencia hasta que el
/// operador la detenga manualmente.
///
/// Deteccion:
///   • Cada 1 segundo, lee el bus de AIRE.
///   • Puede detectar (segun config):
///       - DetectNoChannels: no hay canales activos en el bus.
///       - DetectSilence: hay canales pero su nivel esta por debajo del umbral.
///   • Si alguna condicion habilitada se mantiene N segundos → dispara.
///
/// Al disparar:
///   • Construye una playlist segun Mode (Prearmed / Folder / Hybrid).
///   • Fuerza Mode = Loop (la emergencia debe ser continua).
///   • Reproduce en el bus de AIRE via PlaylistPlayer interno.
///   • Cede al Scheduler (AutoResume del coordinator) si este dispara.
///   • Emite beep cada N tracks (via bus.PlayTestTone).
///
/// Detencion: solo con StopEmergency() (boton del operador).
/// </summary>
public sealed class EmergencyWatchdogService : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly BusExclusivityCoordinator _coordinator;
    private readonly Func<string> _getAirBusId;
    private readonly Action<Action> _dispatch;
    private readonly PlaylistPlayer _player;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private System.Threading.Timer? _firstBeepTimer;
    private EmergencySettings _settings;
    private int _silenceCounterSeconds;
    private int _trackCountSinceBeep;
    private bool _active;
    private bool _disposed;

    /// <summary>
    /// Segundos de espera antes del primer beep tras activarse la emergencia.
    /// Suficiente para que el fade-in del primer track termine y el operador
    /// escuche el beep con claridad sobre la musica.
    /// </summary>
    private const int FirstBeepDelaySeconds = 8;

    public event EventHandler? EmergencyStarted;
    public event EventHandler? EmergencyStopped;
    public event EventHandler<string>? LogMessage;

    /// <summary>
    /// Se dispara cuando cambia la pista en reproduccion (transicion entre
    /// tracks de emergencia, o al iniciar la emergencia por primera vez).
    /// Los ViewModels lo usan para re-notificar sus propiedades derivadas
    /// (LiveStatusText, EmergencyStatusText).
    /// </summary>
    public event EventHandler? NowPlayingChanged;

    /// <summary>
    /// Hook opcional para aplicar EQ/Trim/Norm a los tracks de emergencia.
    /// Se setea desde MainWindowViewModel.
    /// </summary>
    public Action<IAudioChannel, string, bool>? ConfigureChannel { get; set; }

    public bool IsActive { get { lock (_gate) return _active; } }
    public string CurrentTrackTitle { get; private set; } = "";
    public int CurrentTrackIndex { get; private set; }
    public int CurrentTrackTotal { get; private set; }
    public string CurrentBusDisplayName { get; private set; } = "";

    /// <summary>Canales de audio activos de la playlist de emergencia.</summary>
    public IEnumerable<IAudioChannel> GetActiveChannels() => _player.GetActiveChannels();

    public EmergencyWatchdogService(
        IAudioEngine engine,
        BusExclusivityCoordinator coordinator,
        Func<string> getAirBusId,
        Action<Action> dispatch,
        EmergencySettings settings)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _getAirBusId = getAirBusId ?? throw new ArgumentNullException(nameof(getAirBusId));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _settings = (settings ?? new EmergencySettings()).Clone();

        _player = new PlaylistPlayer(
            engine,
            dispatch,
            locution: null,
            coordinator: coordinator);

        _player.ConfigureChannel = (ch, path, logDetails) =>
        {
            try { ConfigureChannel?.Invoke(ch, path, logDetails); } catch { }

            try
            {
                EmergencySettings s;
                lock (_gate) s = _settings;

                // Aplicar volumen especifico de emergencia.
                ch.SetVolume(Math.Clamp(s.Volume, 0.0, 1.0));

                // Aplicar fade especifico de emergencia (override del global).
                double fadeSec = Math.Max(0, s.FadeInMs / 1000.0);
                if (fadeSec > 0.05)
                {
                    ch.SetAutoFade(true, fadeSec);
                }
            }
            catch { }
        };

        // El watchdog usa TrackEnded para reaccionar al fin natural de pista.
        _player.TrackEnded += OnTrackEnded;
        _player.StateChanged += OnPlayerStateChanged;
        _player.SessionEnded += OnSessionEnded;
    }

    // ───── Lifecycle ─────────────────────────────────────────────────────

    public void Start()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(
            OnTick, null,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1));
    }

    public void UpdateSettings(EmergencySettings settings)
    {
        if (settings is null) return;
        lock (_gate)
        {
            _settings = settings.Clone();
            if (!_active) _silenceCounterSeconds = 0;
        }
    }

    public void TriggerNow()
    {
        // Manual: bypass all detection checks.
        FireEmergency("manual");
    }

    /// <summary>
    /// Reproduce el beep manualmente para testear la configuracion.
    /// No dispara emergencia, solo el sonido.
    /// </summary>
    public void TestBeepNow()
    {
        EmergencySettings snapshot;
        lock (_gate) snapshot = _settings;
        PlayBeep(snapshot);
    }

    public void StopEmergency()
    {
        bool fireEvent = false;
        lock (_gate)
        {
            if (!_active) return;
            _active = false;
            _silenceCounterSeconds = 0;
            _trackCountSinceBeep = 0;
            fireEvent = true;
        }

        try { _firstBeepTimer?.Dispose(); } catch { }
        _firstBeepTimer = null;

        try { _player.Stop(); } catch { }

        AppLogger.Info("EMERGENCY", "Emergencia detenida por el operador.");
        Log("Emergencia detenida.");

        if (fireEvent)
            _dispatch(() => { try { EmergencyStopped?.Invoke(this, EventArgs.Empty); } catch { } });
    }

    // ───── Watchdog ──────────────────────────────────────────────────────

    private void OnTick(object? state)
    {
        if (_disposed) return;

        EmergencySettings snapshot;
        lock (_gate) snapshot = _settings;

        if (!snapshot.IsEnabled) return;

        if (IsActive) return; // ya hay emergencia, nada que hacer

        try
        {
            var airBusId = _getAirBusId();
            if (string.IsNullOrEmpty(airBusId)) return;

            var bus = _engine.GetBus(airBusId);
            if (bus is null) return;

            bool shouldCount = false;

            // Check 1: no channels active.
            if (snapshot.DetectNoChannels && bus.ActiveChannelCount == 0)
                shouldCount = true;

            // Check 2: channels active but all silent (below threshold).
            if (!shouldCount && snapshot.DetectSilence)
            {
                var levels = bus.GetCurrentLevels();
                double db = levels.MaxPeak <= 1e-9
                    ? -120.0
                    : 20.0 * Math.Log10(levels.MaxPeak);

                if (db < snapshot.ThresholdDb)
                    shouldCount = true;
            }

            lock (_gate)
            {
                if (_active) return; // race con TriggerNow

                if (shouldCount)
                {
                    _silenceCounterSeconds++;
                    if (_silenceCounterSeconds >= snapshot.SilenceThresholdSeconds)
                    {
                        _silenceCounterSeconds = 0;
                        // Fire fuera del lock.
                        _dispatch(() => FireEmergency("watchdog"));
                    }
                }
                else
                {
                    _silenceCounterSeconds = 0;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning("EMERGENCY", $"Error en watchdog: {ex.Message}");
        }
    }

    // ───── Fire ──────────────────────────────────────────────────────────

    private void FireEmergency(string reason)
    {
        EmergencySettings snapshot;
        lock (_gate)
        {
            if (_active) return;
            snapshot = _settings;
        }

        try
        {
            var airBusId = _getAirBusId();
            if (string.IsNullOrEmpty(airBusId))
            {
                Log("Bus de AIRE no configurado. Emergencia cancelada.");
                return;
            }

            var bus = _engine.GetBus(airBusId);
            if (bus is null)
            {
                Log($"Bus '{airBusId}' no encontrado. Emergencia cancelada.");
                return;
            }

            var playlist = BuildEmergencyPlaylist(snapshot);
            if (playlist is null || playlist.Tracks.Count == 0)
            {
                Log("No hay archivos de emergencia. Revisar la configuracion.");
                return;
            }

            lock (_gate)
            {
                if (_active) return;
                _active = true;
                _silenceCounterSeconds = 0;
                _trackCountSinceBeep = 0;
            }

            _player.SetBus(bus);
            _player.Play(playlist, 0);
            CurrentBusDisplayName = bus.DisplayName;

            UpdateTrackInfo();

            // Programar el PRIMER beep a los N segundos. Lo separamos de
            // la logica track-based para que suene durante la reproduccion
            // del primer track, no en la transicion (donde el fade-in lo
            // taparia).
            ScheduleFirstBeep(snapshot);

            AppLogger.Warning("EMERGENCY",
                $"🚨 EMERGENCIA ACTIVADA ({reason}): {playlist.Tracks.Count} pista(s) en bus '{bus.DisplayName}'.");
            Log($"🚨 Emergencia activada ({reason}) en '{bus.DisplayName}'.");

            _dispatch(() => { try { EmergencyStarted?.Invoke(this, EventArgs.Empty); } catch { } });
        }
        catch (Exception ex)
        {
            lock (_gate) _active = false;
            AppLogger.Error("EMERGENCY", $"Error al activar emergencia: {ex.Message}", ex);
            Log($"Error: {ex.Message}");
        }
    }

    // ───── Playlist building ─────────────────────────────────────────────

    private Playlist? BuildEmergencyPlaylist(EmergencySettings snapshot)
    {
        if (snapshot.Mode == EmergencySourceMode.Prearmed ||
            snapshot.Mode == EmergencySourceMode.Hybrid)
        {
            var prearmed = TryLoadPrearmed(snapshot.PrearmedPlaylistPath);
            if (prearmed is not null) return prearmed;
            if (snapshot.Mode == EmergencySourceMode.Prearmed) return null;
        }

        return BuildFromFolder(snapshot.EmergencyFolder, snapshot.IncludeSubfolders);
    }

    private Playlist? TryLoadPrearmed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        try
        {
            var loaded = PlaylistStore.Load(path);
            if (loaded is null || loaded.Tracks.Count == 0) return null;

            var tracks = loaded.Tracks
                .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
                .ToList();

            if (tracks.Count == 0) return null;

            return new Playlist
            {
                Name = "🚨 EMERGENCIA",
                Mode = PlaylistMode.Loop,
                Tracks = tracks
            };
        }
        catch
        {
            return null;
        }
    }

    private Playlist? BuildFromFolder(string folder, bool includeSub)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        if (!Directory.Exists(folder)) return null;

        try
        {
            var searchOpt = includeSub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(folder, "*.*", searchOpt)
                .Where(f => IsAudioExtension(f))
                .ToList();

            if (files.Count == 0) return null;

            // Shuffle
            var rng = Random.Shared;
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (files[i], files[j]) = (files[j], files[i]);
            }

            var tracks = files.Select(f => new PlaylistTrack
            {
                FilePath = f,
                Title = Path.GetFileNameWithoutExtension(f),
                IsEnabled = true
            }).ToList();

            return new Playlist
            {
                Name = "🚨 EMERGENCIA",
                Mode = PlaylistMode.Loop,
                Tracks = tracks
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAudioExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" or ".mp2" or ".mp1" or
            ".ogg" or ".oga" or
            ".wav" or ".aif" or ".aiff" or
            ".flac" or ".fla" or
            ".m4a" or ".m4b" or ".aac" or ".mp4" or
            ".opus" or ".wv" or ".rmw" => true,
            _ => false
        };
    }

    // ───── Player event handlers ─────────────────────────────────────────

    private void OnTrackEnded(object? sender, int trackIndex)
    {
        EmergencySettings snapshot;
        lock (_gate) snapshot = _settings;

        // Actualizar info de pista aunque no haya beep.
        UpdateTrackInfo();

        if (!snapshot.BeepEnabled) return;
        if (snapshot.BeepEveryNTracks <= 0) return;

        _trackCountSinceBeep++;
        if (_trackCountSinceBeep >= snapshot.BeepEveryNTracks)
        {
            _trackCountSinceBeep = 0;
            PlayBeep(snapshot);
        }
    }

    private void OnPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        UpdateTrackInfo();
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        // La playlist de emergencia termino inesperadamente (no deberia, porque
        // esta en modo Loop). Resetear el flag para re-armar el watchdog.
        try { _firstBeepTimer?.Dispose(); } catch { }
        _firstBeepTimer = null;

        lock (_gate)
        {
            if (!_active) return;
            _active = false;
            _silenceCounterSeconds = 0;
        }

        AppLogger.Warning("EMERGENCY",
            "Playlist de emergencia termino inesperadamente. Watchdog re-armado.");
        Log("Emergencia finalizada inesperadamente. Re-armando watchdog.");

        _dispatch(() => { try { EmergencyStopped?.Invoke(this, EventArgs.Empty); } catch { } });
    }

    private void ScheduleFirstBeep(EmergencySettings snapshot)
    {
        try { _firstBeepTimer?.Dispose(); } catch { }
        _firstBeepTimer = null;

        if (!snapshot.BeepEnabled) return;

        _firstBeepTimer = new System.Threading.Timer(_ =>
        {
            bool stillActive;
            EmergencySettings s;
            lock (_gate)
            {
                stillActive = _active;
                s = _settings;
            }

            if (!stillActive) return;
            if (!s.BeepEnabled) return;

            PlayBeep(s);

            // Resetear el contador para que los beeps posteriores
            // (por cada N tracks) arranquen a contar desde aca.
            lock (_gate) _trackCountSinceBeep = 0;

        }, null, TimeSpan.FromSeconds(FirstBeepDelaySeconds), Timeout.InfiniteTimeSpan);
    }

    private void PlayBeep(EmergencySettings snapshot)
    {
        try
        {
            var airBusId = _getAirBusId();
            if (string.IsNullOrEmpty(airBusId)) return;

            var bus = _engine.GetBus(airBusId);
            if (bus is null) return;

            // 4 pulsos de 500ms separados por 150ms, amplitud 0.9 (muy audible).
            // Total: ~2.3 segundos. Se escucha claramente sobre la musica de fondo.
            bus.PlayTestToneSequence(
                frequencyHz: snapshot.BeepFrequencyHz,
                pulseMs: snapshot.BeepDurationMs > 0 ? snapshot.BeepDurationMs : 500,
                gapMs: 150,
                pulseCount: 4,
                amplitude: 0.9);

            AppLogger.Debug("EMERGENCY", "🔔 Beep de emergencia (4 pulsos).");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("EMERGENCY", $"Error en beep: {ex.Message}");
        }
    }

    private void UpdateTrackInfo()
    {
        try
        {
            var playlist = _player.CurrentPlaylist;
            var index = _player.CurrentIndex;

            CurrentTrackTotal = playlist?.Tracks.Count ?? 0;
            CurrentTrackIndex = index >= 0 ? index + 1 : 0;
            CurrentTrackTitle = (playlist is not null && index >= 0 && index < playlist.Tracks.Count)
                ? playlist.Tracks[index].DisplayTitle
                : "";
        }
        catch { }

        // Notificar a los suscriptores que cambio la pista en curso.
        try { NowPlayingChanged?.Invoke(this, EventArgs.Empty); } catch { }
    }

    private void Log(string message)
    {
        try { LogMessage?.Invoke(this, message); } catch { }
    }

    /// <summary>
    /// Devuelve true si la configuración de emergencia está completa y
    /// puede armarse. Se valida:
    ///   - Al menos una detección activa (no channels / silence).
    ///   - Umbral de silencio válido (> 0 segundos).
    ///   - Threshold dB en rango físico (-90 a -20).
    ///   - Fuente válida según el modo:
    ///       • Prearmed: el .rmf existe en disco.
    ///       • Folder: la carpeta existe y tiene al menos 1 audio.
    ///       • Hybrid: al menos uno de los dos.
    /// </summary>
    public bool IsConfigurationValid()
    {
        EmergencySettings s;
        lock (_gate) s = _settings;

        if (s is null) return false;

        if (!s.DetectNoChannels && !s.DetectSilence) return false;
        if (s.SilenceThresholdSeconds <= 0) return false;
        if (s.ThresholdDb < -90.0 || s.ThresholdDb > -20.0) return false;

        return s.Mode switch
        {
            EmergencySourceMode.Prearmed =>
                !string.IsNullOrWhiteSpace(s.PrearmedPlaylistPath)
                && File.Exists(s.PrearmedPlaylistPath),

            EmergencySourceMode.Folder =>
                IsValidEmergencyFolder(s.EmergencyFolder, s.IncludeSubfolders),

            EmergencySourceMode.Hybrid =>
                (!string.IsNullOrWhiteSpace(s.PrearmedPlaylistPath)
                 && File.Exists(s.PrearmedPlaylistPath))
                || IsValidEmergencyFolder(s.EmergencyFolder, s.IncludeSubfolders),

            _ => false
        };
    }

    private static bool IsValidEmergencyFolder(string folder, bool includeSub)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        if (!Directory.Exists(folder)) return false;

        try
        {
            var searchOpt = includeSub
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            return Directory.EnumerateFiles(folder, "*.*", searchOpt)
                .Any(f => IsAudioExtension(f));
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _timer?.Dispose(); } catch { }
        _timer = null;

        try { _firstBeepTimer?.Dispose(); } catch { }
        _firstBeepTimer = null;

        try { _player.TrackEnded -= OnTrackEnded; } catch { }
        try { _player.StateChanged -= OnPlayerStateChanged; } catch { }
        try { _player.SessionEnded -= OnSessionEnded; } catch { }

        try { StopEmergency(); } catch { }
        try { _player.Dispose(); } catch { }
    }
}
