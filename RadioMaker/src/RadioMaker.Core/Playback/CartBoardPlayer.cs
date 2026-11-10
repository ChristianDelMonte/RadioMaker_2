using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Scheduling;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Playback;

/// <summary>
/// Reproductor de la botonera (Modulo 3B).
///
/// A diferencia del PlaylistPlayer (que es secuencial y exclusivo), la
/// botonera reproduce botones de forma INDEPENDIENTE y SIMULTANEA. Cada
/// boton tiene su propio canal, y se puede tener varios sonando a la vez.
///
/// Comportamiento normal:
///   - Play(button): si el boton no estaba sonando, arranca una nueva
///     reproduccion. Si YA estaba sonando, lo detiene (toggle).
///   - Stop(button): detiene solo ese boton.
///   - StopAll(): detiene todos los botones activos.
///
/// Modo crossfade (opt-in):
///   - Cuando <see cref="IsCrossfadeEnabled"/> esta activo, al disparar
///     un boton mientras hay otros sonando, se hace fade-out de todos
///     los activos y fade-in del nuevo. La duracion del fade es
///     <see cref="CrossfadeDurationMs"/>.
///   - De esta forma no quedan varios audios superpuestos.
///
/// Integracion con el resto:
///   - ConfigureChannel hook: aplica EQ/Trim/Norm automaticos a cada boton.
///   - Un bus de salida configurable (por defecto el primario).
/// </summary>
public sealed class CartBoardPlayer : IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly Action<Action> _dispatch;
    private readonly BusExclusivityCoordinator? _coordinator;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private string _acquiredBusId = string.Empty;

    private readonly object _gate = new();

    private IAudioBus? _targetBus;

    /// <summary>Mapa buttonId -> canal actualmente sonando por ese boton.</summary>
    private readonly Dictionary<string, IAudioChannel> _activeChannels = new();
    private readonly Dictionary<string, IAudioChannel> _suspendedChannels = new();

    // ───── Configuracion de crossfade ────────────────────────────────────

    private bool _isCrossfadeEnabled;
    private int _crossfadeDurationMs = 500;

    /// <summary>
    /// Cuando esta activo, al disparar un cart mientras suena otro se hace
    /// un crossfade (fade-out del actual + fade-in del nuevo).
    /// </summary>
    public bool IsCrossfadeEnabled
    {
        get => _isCrossfadeEnabled;
        set
        {
            bool changed;
            lock (_gate)
            {
                changed = _isCrossfadeEnabled != value;
                if (changed) _isCrossfadeEnabled = value;
            }

            if (changed)
            {
                try { CrossfadeStateChanged?.Invoke(this, EventArgs.Empty); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Duracion del crossfade en milisegundos. Default: 500 ms.
    /// Valores muy chicos (menos de 100 ms) pueden sonar bruscos.
    /// </summary>
    public int CrossfadeDurationMs
    {
        get => _crossfadeDurationMs;
        set
        {
            int clamped = Math.Clamp(value, 50, 5000);
            lock (_gate) _crossfadeDurationMs = clamped;
        }
    }

    /// <summary>
    /// Hook para aplicar EQ/Trim/Norm antes de cada Play.
    /// Parametros: (canal, ruta del archivo, loguear detalles).
    /// </summary>
    public Action<IAudioChannel, string, bool>? ConfigureChannel { get; set; }

    // ───── Eventos ───────────────────────────────────────────────────────

    /// <summary>Se dispara cuando un boton empieza o deja de sonar.</summary>
    public event EventHandler<CartButtonPlaybackEvent>? ButtonStateChanged;
    /// <summary>
    /// Se dispara cuando cambia IsCrossfadeEnabled (por ejemplo, porque
    /// el usuario lo activo desde el panel de configuracion).
    /// Las VMs se suscriben para refrescar su propia copia del estado.
    /// </summary>
    public event EventHandler? CrossfadeStateChanged;

    // ───── Constructor ───────────────────────────────────────────────────

    public CartBoardPlayer(
        IAudioEngine audioEngine,
        Action<Action> dispatch,
        BusExclusivityCoordinator? coordinator = null)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _coordinator = coordinator;

        if (_coordinator is not null)
        {
            _coordinator.ChannelExpropriated += OnChannelExpropriated;
            _coordinator.ChannelResumed += OnChannelResumed;
        }
    }

    // ───── Configuracion ─────────────────────────────────────────────────

    public void SetBus(IAudioBus? bus)
    {
        lock (_gate)
        {
            _targetBus = bus;

            // Si cambia el bus, libero el anterior. Los canales activos del
            // bus viejo siguen sonando (huerfanos) hasta que terminen.
            if (!string.IsNullOrEmpty(_acquiredBusId) &&
                (bus is null || !string.Equals(bus.Id, _acquiredBusId, StringComparison.OrdinalIgnoreCase)))
            {
                ReleaseBusInternal();
            }
        }
    }

    public bool IsPlaying(string buttonId)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(buttonId)) return false;
            if (!_activeChannels.TryGetValue(buttonId, out var ch)) return false;
            if (ch is null) return false;

            try { return ch.State == Abstractions.PlaybackState.Playing; }
            catch { return false; }
        }
    }

    public bool HasAnyPlaying
    {
        get
        {
            lock (_gate)
            {
                return _activeChannels.Values.Any(ch =>
                {
                    try { return ch.State == Abstractions.PlaybackState.Playing; }
                    catch { return false; }
                });
            }
        }
    }

    /// <summary>
    /// True si hay canales suspendidos por el coordinator (AutoResume).
    /// Un cart suspendido esta pausado, no reproduciendo. La pestaña que
    /// lo contiene debe mostrar el indicador de "pausado" (naranja) en
    /// lugar del indicador de "al aire" (rojo).
    /// </summary>
    public bool HasSuspendedChannels
    {
        get
        {
            lock (_gate) return _suspendedChannels.Count > 0;
        }
    }

    public IAudioChannel? CurrentChannel
    {
        get
        {
            lock (_gate)
            {
                foreach (var ch in _activeChannels.Values)
                {
                    try
                    {
                        if (ch.State == Abstractions.PlaybackState.Playing)
                            return ch;
                    }
                    catch { }
                }
                return null;
            }
        }
    }

    public IEnumerable<IAudioChannel> GetActiveChannels()
    {
        var list = new List<IAudioChannel>();

        lock (_gate)
        {
            foreach (var ch in _activeChannels.Values)
            {
                if (ch is not null)
                    list.Add(ch);
            }
        }

        return list;
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    public void TogglePlay(CartButton button)
    {
        if (button is null) return;
        if (!button.IsEnabled) return;
        if (!button.HasFile) return;

        if (IsPlaying(button.Id))
        {
            Stop(button);
        }
        else
        {
            Play(button);
        }
    }

    /// <summary>
    /// Arranca la reproduccion del boton en un canal propio.
    ///
    /// Si el modo crossfade esta activo y hay otros botones sonando, se
    /// hace fade-out de esos canales y fade-in del nuevo.
    ///
    /// Si el crossfade esta desactivado, el nuevo canal se suma a los que
    /// ya estan sonando (comportamiento clasico).
    /// </summary>
    public void Play(CartButton button)
    {
        if (button is null) return;
        if (!button.IsEnabled) return;

        if (string.IsNullOrWhiteSpace(button.FilePath))
        {
            AppLogger.Warning("CART",
                $"Intento de reproducir boton sin archivo asignado: '{button.DisplayLabel}'");
            return;
        }

        if (!File.Exists(button.FilePath))
        {
            AppLogger.Warning("CART",
                $"Archivo no encontrado para boton '{button.DisplayLabel}': {button.FilePath}");
            return;
        }

        _dispatch(() =>
        {
            lock (_gate)
            {
                // Al disparar un boton, cancelar cualquier suspendido pendiente.
                // (Suspended = expropiado por AutoResume esperando reanudacion;
                // si el usuario aprieta un boton, su intencion es empezar de nuevo.)
                foreach (var susp in _suspendedChannels.Values)
                {
                    try { susp.Dispose(); } catch { }
                }
                _suspendedChannels.Clear();

                // Detener la reproduccion previa de este MISMO boton.
                StopInternal(button.Id);

                // Leer config ANTES de tocar canales (podria cambiar entre awaits).
                bool useCrossfade = _isCrossfadeEnabled;
                int crossfadeMs = _crossfadeDurationMs;

                // Si crossfade esta activo, hacer fade out de TODOS los
                // demas canales activos (concurrente con el fade in del nuevo).
                if (useCrossfade)
                {
                    FadeOutAllExcept(button.Id, crossfadeMs);
                }

                var bus = _targetBus ?? _audioEngine.PrimaryBus;

                try
                {
                    var channel = bus.LoadFile(button.FilePath);

                    // ===== EXCLUSIVIDAD DE BUS =====
                    var acquire = TryAcquireBus(bus.Id, channel);
                    if (acquire.Denied)
                    {
                        AppLogger.Warning("CART",
                            $"Reproducción rechazada: bus '{bus.DisplayName}' ocupado por mayor prioridad.");
                        try { channel.Dispose(); } catch { }
                        RaiseStateChanged(button.Id, isPlaying: false);
                        return;
                    }

                    // Log de inicio de reproduccion del cart.
                    var deviceName = bus.DeviceDisplayName;
                    var fileName = Path.GetFileName(button.FilePath);

                    AppLogger.Info("CART PLAY",
                        $"Device: {deviceName} | " +
                        $"Reproduciendo: {fileName} | " +
                        $"Label: {button.DisplayLabel}" +
                        (string.IsNullOrWhiteSpace(button.Hotkey) ? "" : $" | Hotkey: {button.Hotkey}") +
                        (useCrossfade ? $" | CROSSFADE {crossfadeMs}ms" : ""));

                    // Aplicar EQ/Trim/Norm (logueando detalles).
                    try { ConfigureChannel?.Invoke(channel, button.FilePath, true); } catch { }

                    // Volumen adicional del boton.
                    if (Math.Abs(button.VolumeDb) > 0.01)
                    {
                        try
                        {
                            double currentNorm = channel.NormalizationGainDb;
                            channel.SetNormalizationGain(currentNorm + button.VolumeDb);
                        }
                        catch { }
                    }

                // Suscribir al fin.
                channel.Ended += (_, _) => OnButtonEnded(button.Id);

                // Arrancar con o sin fade-in.
                // Arrancar con fade-in si: (a) hay crossfade configurado
                // O (b) expropiamos a otro módulo (transición suave siempre).
                bool forceFadeIn = acquire.ExpropriatedOwnerId is not null;
                if ((useCrossfade || forceFadeIn) && crossfadeMs > 0)
                {
                    channel.FadeIn(crossfadeMs, 1.0);
                }
                else
                {
                    channel.Play();
                }

                _activeChannels[button.Id] = channel;

                RaiseStateChanged(button.Id, isPlaying: true);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("CART",
                        $"Error reproduciendo boton '{button.DisplayLabel}': {ex.Message}", ex);
                    RaiseStateChanged(button.Id, isPlaying: false);
                }
            }
        });
    }

    /// <summary>Detiene la reproduccion del boton indicado.</summary>
    public void Stop(CartButton button)
    {
        if (button is null) return;

        _dispatch(() =>
        {
            lock (_gate)
            {
                StopInternal(button.Id);
                RaiseStateChanged(button.Id, isPlaying: false);
            }
        });
    }

    /// <summary>Detiene todos los botones activos.</summary>
    public void StopAll()
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                foreach (var id in _activeChannels.Keys.ToList())
                {
                    StopInternal(id);
                    RaiseStateChanged(id, isPlaying: false);
                }
            }
        });
    }

    /// <summary>
    /// Ajusta el volumen de un boton que esta sonando. Si no esta sonando,
    /// el cambio se aplicara la proxima vez que se reproduzca.
    /// </summary>
    public void SetLiveVolume(string buttonId, double volumeDb)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (!_activeChannels.TryGetValue(buttonId, out var ch)) return;
                if (ch is null) return;

                try
                {
                    ch.SetNormalizationGain(volumeDb);
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Reaplica la configuracion (EQ/Trim/Norm) SOLO a los canales activos
    /// cuyo archivo coincide con <paramref name="filePath"/>.
    /// </summary>
    public void ReapplyToMatchingChannels(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        _dispatch(() =>
        {
            lock (_gate)
            {
                foreach (var kvp in _activeChannels.ToList())
                {
                    var ch = kvp.Value;
                    if (ch is null) continue;

                    var chPath = ch.FilePath;
                    if (string.IsNullOrEmpty(chPath)) continue;
                    if (!string.Equals(chPath, filePath, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        ConfigureChannel?.Invoke(ch, filePath, false);
                    }
                    catch { }
                }
            }
        });
    }

    // ───── Crossfade ─────────────────────────────────────────────────────

    /// <summary>
    /// Hace fade-out de todos los canales activos EXCEPTO el que se va a
    /// reemplazar (que ya fue detenido antes de llamar a este metodo).
    ///
    /// El canal sigue en el diccionario mientras dura el fade; cuando el
    /// fade termina, se dispone y se quita del diccionario. Esto evita
    /// que dos disparos muy seguidos dejen canales "zombie".
    /// </summary>
    private void FadeOutAllExcept(string keepButtonId, int fadeMs)
    {
        foreach (var kvp in _activeChannels.ToList())
        {
            if (kvp.Key == keepButtonId) continue;

            var ch = kvp.Value;
            if (ch is null) continue;

            var id = kvp.Key;

            try
            {
                ch.FadeOut(fadeMs, () =>
                {
                    _dispatch(() =>
                    {
                        lock (_gate)
                        {
                            // Solo dispose/remove si este canal sigue siendo
                            // el activo de ese boton (pudo haber sido reemplazado
                            // por un disparo mas reciente).
                            if (_activeChannels.TryGetValue(id, out var cur) &&
                                ReferenceEquals(cur, ch))
                            {
                                try { ch.Dispose(); } catch { }
                                _activeChannels.Remove(id);
                                RaiseStateChanged(id, isPlaying: false);
                            }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                // Si falla el fade, hacemos stop directo para no dejar el
                // audio colgado.
                AppLogger.Warning("CART",
                    $"Fallo el fade-out del boton {id}: {ex.Message}");
                try { ch.Stop(); ch.Dispose(); } catch { }
                _activeChannels.Remove(id);
                RaiseStateChanged(id, isPlaying: false);
            }
        }
    }

    // ───── Internals ─────────────────────────────────────────────────────

    private void StopInternal(string buttonId)
    {
        if (string.IsNullOrEmpty(buttonId)) return;

        // Chequear primero suspendidos (puede estar expropiado y pausado).
        if (_suspendedChannels.TryGetValue(buttonId, out var susp))
        {
            _suspendedChannels.Remove(buttonId);
            try { susp.Dispose(); } catch { }
            return;
        }

        if (!_activeChannels.TryGetValue(buttonId, out var ch)) return;

        try
        {
            if (ch is not null)
            {
                ch.Stop();
                ch.Dispose();
            }
        }
        catch { }

        _activeChannels.Remove(buttonId);

        if (_activeChannels.Count == 0 && _suspendedChannels.Count == 0)
            ReleaseBusInternal();
    }

    private void OnButtonEnded(string buttonId)
    {
        _dispatch(() =>
        {
            lock (_gate)
            {
                StopInternal(buttonId);
                RaiseStateChanged(buttonId, isPlaying: false);

                AppLogger.Info("CART PLAY", $"Fin de reproduccion del boton {buttonId}");
            }
        });
    }

    private void RaiseStateChanged(string buttonId, bool isPlaying)
    {
        try
        {
            ButtonStateChanged?.Invoke(this, new CartButtonPlaybackEvent(buttonId, isPlaying));
        }
        catch { }
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_coordinator is not null)
        {
            _coordinator.ChannelExpropriated -= OnChannelExpropriated;
            _coordinator.ChannelResumed -= OnChannelResumed;
        }

        lock (_gate)
        {
            foreach (var id in _activeChannels.Keys.ToList())
                StopInternal(id);

            foreach (var susp in _suspendedChannels.Values)
            {
                try { susp.Dispose(); } catch { }
            }
            _suspendedChannels.Clear();

            ReleaseBusInternal();
        }
    }

    // ===== EXCLUSIVIDAD DE BUS =====

    private BusAcquireResult TryAcquireBus(string busId, IAudioChannel channel)
    {
        if (_coordinator is null) return BusAcquireResult.GrantedNoConflict;

        if (!string.IsNullOrEmpty(_acquiredBusId) &&
            string.Equals(_acquiredBusId, busId, StringComparison.OrdinalIgnoreCase))
            return BusAcquireResult.GrantedNoConflict;

        // Cambio de bus: liberar el viejo primero.
        if (!string.IsNullOrEmpty(_acquiredBusId))
            ReleaseBusInternal();

        var result = _coordinator.TryAcquire(busId, _instanceId, ModuleRole.Cart, channel);
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
                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);

                var channelsToHandle = _activeChannels.ToList();
                _activeChannels.Clear();
                _acquiredBusId = string.Empty;

                foreach (var kvp in channelsToHandle)
                    RaiseStateChanged(kvp.Key, isPlaying: false);

                // AutoResume: pausar y guardar en suspendidos.
                // Pause: fallback a Stop (Cart no tiene "reanudar manual"
                // por boton; el usuario aprieta de nuevo para reiniciar).
                // Stop: fade-out + dispose.
                bool isAutoResume = e.Behavior == InterruptionBehavior.AutoResume;

                foreach (var kvp in channelsToHandle)
                {
                    var id = kvp.Key;
                    var ch = kvp.Value;
                    if (ch is null) continue;

                    if (isAutoResume)
                    {
                        try
                        {
                            ch.FadeOutAndPause(ms);
                            _suspendedChannels[id] = ch;
                        }
                        catch
                        {
                            try { ch.Dispose(); } catch { }
                        }
                    }
                    else
                    {
                        try { ch.FadeOut(ms, () => { try { ch.Dispose(); } catch { } }); }
                        catch { try { ch.Dispose(); } catch { } }
                    }
                }

                AppLogger.Info("CART",
                    $"Cart expropiado del bus '{e.BusId}' por '{e.Role}' " +
                    $"({(isAutoResume ? "AutoResume" : "Stop")}).");
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
                if (_suspendedChannels.Count == 0) return;

                var acquire = _coordinator.TryAcquire(
                    e.BusId, _instanceId, ModuleRole.Cart, null!);
                if (acquire.Denied) return;
                _acquiredBusId = e.BusId;

                int ms = Math.Max(50, _coordinator.CrossfadeDurationMs);

                foreach (var kvp in _suspendedChannels.ToList())
                {
                    var id = kvp.Key;
                    var ch = kvp.Value;
                    if (ch is null) continue;

                    try
                    {
                        ch.FadeIn(ms, 1.0, preservePosition: true);
                        _activeChannels[id] = ch;
                        RaiseStateChanged(id, isPlaying: true);
                    }
                    catch
                    {
                        try { ch.Dispose(); } catch { }
                    }
                }

                _suspendedChannels.Clear();

                AppLogger.Info("CART",
                    $"Cart reanudado en bus '{e.BusId}' tras finalizar '{e.Role}'.");
            }
        });
    }
}

/// <summary>
/// Argumentos del evento ButtonStateChanged del CartBoardPlayer.
/// </summary>
public sealed record CartButtonPlaybackEvent(string ButtonId, bool IsPlaying);