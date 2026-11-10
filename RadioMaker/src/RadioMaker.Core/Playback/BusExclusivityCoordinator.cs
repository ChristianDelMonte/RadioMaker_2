using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Playback;

/// <summary>
/// Coordinador central de exclusividad de buses.
///
/// MODELO DE Behavior (Phase 2B.1):
///   El <see cref="ModuleExclusivityPolicy.Behavior"/> describe COMO TRATA
///   EL EXPOPIADOR al expropiado cuando lo saca del bus. No es "como quiere
///   ser tratado el expropiado".
///
///   Ejemplos:
///     • Scheduler.Behavior = AutoResume → cuando el Scheduler expropia a
///       un Cart/Playlist, lo pausa y lo reanuda al terminar.
///     • Cart.Behavior = Stop → cuando un Cart expropia a otro Cart, lo
///       detiene (no lo pausa).
///
///   Esto evita la ambiguedad "el mismo expropiado puede querer cosas
///   distintas segun quien lo expropie". El comportamiento se define
///   por el lado del ganador.
/// </summary>
public sealed class BusExclusivityCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Occupant> _occupants = new();
    private readonly Dictionary<string, Stack<SuspendedOwner>> _suspendedByBus = new();
    private readonly BusExclusivityConfig _config;

    public int CrossfadeDurationMs { get; set; } = 500;

    public event EventHandler<ChannelExpropriatedEventArgs>? ChannelExpropriated;
    public event EventHandler<ChannelResumedEventArgs>? ChannelResumed;

    public BusExclusivityCoordinator(BusExclusivityConfig? config = null)
    {
        _config = config ?? new BusExclusivityConfig();
        EnsureDefaultPolicies();
    }

    public BusExclusivityConfig Config => _config;

    public BusAcquireResult TryAcquire(
        string busId,
        string ownerId,
        ModuleRole role,
        IAudioChannel channel)
    {
        if (string.IsNullOrEmpty(busId))
            return BusAcquireResult.GrantedNoConflict;

        var myPolicy = _config.GetPolicy(role);

        if (!myPolicy.IsExclusive)
            return BusAcquireResult.GrantedNoConflict;

        Occupant? expropriated = null;
        InterruptionBehavior behaviorToApply = myPolicy.Behavior;

        lock (_gate)
        {
            if (!_occupants.TryGetValue(busId, out var current))
            {
                _occupants[busId] = new Occupant
                {
                    BusId = busId,
                    OwnerId = ownerId,
                    Role = role,
                    Channel = channel,
                    Priority = myPolicy.Priority
                };
                return BusAcquireResult.GrantedNoConflict;
            }

            if (string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal))
                return BusAcquireResult.GrantedNoConflict;

            if (myPolicy.Priority < current.Priority)
            {
                AppLogger.Info("EXCLUSIVITY",
                    $"'{role}' ({ownerId}) denegado en bus '{busId}': " +
                    $"'{current.Role}' ({current.OwnerId}) tiene mayor prioridad.");
                return BusAcquireResult.DeniedResult;
            }

            expropriated = current;

            _occupants[busId] = new Occupant
            {
                BusId = busId,
                OwnerId = ownerId,
                Role = role,
                Channel = channel,
                Priority = myPolicy.Priority
            };

            // Si YO (expropiador) tengo AutoResume, pushear al expropiado al stack.
            if (behaviorToApply == InterruptionBehavior.AutoResume)
            {
                if (!_suspendedByBus.TryGetValue(busId, out var stack))
                {
                    stack = new Stack<SuspendedOwner>();
                    _suspendedByBus[busId] = stack;
                }
                stack.Push(new SuspendedOwner
                {
                    OwnerId = current.OwnerId,
                    Role = current.Role
                });
            }
        }

        if (expropriated is not null)
        {
            AppLogger.Info("EXCLUSIVITY",
                $"'{role}' ({ownerId}) expropió bus '{busId}' de " +
                $"'{expropriated.Role}' ({expropriated.OwnerId}). " +
                $"Behavior del expropiador: {behaviorToApply}.");

            NotifyExpropriated(expropriated, behaviorToApply);

            return new BusAcquireResult
            {
                Granted = true,
                ExpropriatedOwnerId = expropriated.OwnerId,
                ExpropriatedRole = expropriated.Role,
                ExpropriatedBehavior = behaviorToApply
            };
        }

        return BusAcquireResult.GrantedNoConflict;
    }

    public void Release(string busId, string ownerId)
    {
        if (string.IsNullOrEmpty(busId) || string.IsNullOrEmpty(ownerId)) return;

        SuspendedOwner? nextToResume = null;

        lock (_gate)
        {
            if (!_occupants.TryGetValue(busId, out var current)) return;
            if (!string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal)) return;

            _occupants.Remove(busId);

            if (_suspendedByBus.TryGetValue(busId, out var stack) && stack.Count > 0)
            {
                nextToResume = stack.Pop();
                if (stack.Count == 0)
                    _suspendedByBus.Remove(busId);
            }
        }

        if (nextToResume is not null)
        {
            AppLogger.Info("EXCLUSIVITY",
                $"Bus '{busId}' liberado. Reanudando '{nextToResume.Role}' ({nextToResume.OwnerId}).");
            NotifyResumed(busId, nextToResume);
        }
    }

    public bool IsBusOccupied(string busId)
    {
        lock (_gate) return _occupants.ContainsKey(busId);
    }

    public string? GetCurrentOwnerId(string busId)
    {
        lock (_gate)
        {
            return _occupants.TryGetValue(busId, out var o) ? o.OwnerId : null;
        }
    }

    /// <summary>
    /// Resetea todas las politicas a los valores por defecto. Util para la UI.
    /// </summary>
    public void ResetPoliciesToDefaults()
    {
        lock (_gate)
        {
            _config.Policies.Clear();
            EnsureDefaultPolicies();
        }
    }

    private void NotifyExpropriated(Occupant expropriated, InterruptionBehavior behavior)
    {
        try
        {
            ChannelExpropriated?.Invoke(this, new ChannelExpropriatedEventArgs
            {
                BusId = expropriated.BusId,
                OwnerId = expropriated.OwnerId,
                Role = expropriated.Role,
                Behavior = behavior
            });
        }
        catch { }
    }

    private void NotifyResumed(string busId, SuspendedOwner owner)
    {
        try
        {
            ChannelResumed?.Invoke(this, new ChannelResumedEventArgs
            {
                BusId = busId,
                OwnerId = owner.OwnerId,
                Role = owner.Role
            });
        }
        catch { }
    }

    private void EnsureDefaultPolicies()
    {
        foreach (ModuleRole role in Enum.GetValues(typeof(ModuleRole)))
        {
            if (_config.Policies.ContainsKey(role)) continue;

            _config.Policies[role] = role switch
            {
                // FileManager / FavoriteFolder: no exclusivos por defecto
                // (preescucha paralela), Stop si llegan a ser exclusivos.
                ModuleRole.FileManager => new ModuleExclusivityPolicy
                {
                    IsExclusive = false,
                    Priority = ModulePriority.Normal,
                    Behavior = InterruptionBehavior.Stop
                },
                ModuleRole.FavoriteFolder => new ModuleExclusivityPolicy
                {
                    IsExclusive = false,
                    Priority = ModulePriority.Normal,
                    Behavior = InterruptionBehavior.Stop
                },
                // Scheduler: prioridad AIRE y AutoResume. Cuando el Scheduler
                // arranca, pausa lo que este sonando; cuando termina, lo reanuda.
                ModuleRole.Scheduler => new ModuleExclusivityPolicy
                {
                    IsExclusive = true,
                    Priority = ModulePriority.Air,
                    Behavior = InterruptionBehavior.AutoResume
                },
                // Emergencia: Si esta activado prioridad ante todo
                ModuleRole.Emergency => new ModuleExclusivityPolicy
                {
                    IsExclusive = true,
                    Priority = ModulePriority.High,
                    Behavior = InterruptionBehavior.Stop
                },
                // Cart / Playlist / Manual: Stop. Cuando un Cart saca a otro
                // Cart, lo detiene (como hoy). Cuando una Playlist saca a otra
                // Playlist, la detiene.
                _ => new ModuleExclusivityPolicy
                {
                    IsExclusive = true,
                    Priority = ModulePriority.Normal,
                    Behavior = InterruptionBehavior.Stop
                }
            };
        }
    }

    private sealed class Occupant
    {
        public string BusId { get; init; } = "";
        public string OwnerId { get; init; } = "";
        public ModuleRole Role { get; init; }
        public IAudioChannel Channel { get; init; } = null!;
        public ModulePriority Priority { get; init; }
    }

    private sealed class SuspendedOwner
    {
        public string OwnerId { get; init; } = "";
        public ModuleRole Role { get; init; }
    }
}