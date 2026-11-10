namespace RadioMaker.Core.Abstractions;

public enum ModuleRole
{
    Cart = 0,
    Playlist = 1,
    Scheduler = 2,
    FileManager = 3,
    FavoriteFolder = 4,
    Manual = 5,
    Emergency = 6
}

public enum InterruptionBehavior
{
    /// <summary>Fade-out + dispose. Se descarta.</summary>
    Stop = 0,

    /// <summary>Fade-out + pause. El usuario lo reanuda manualmente.</summary>
    Pause = 1,

    /// <summary>Fade-out + pause + auto-resume al liberar el bus.</summary>
    AutoResume = 2
}

public enum ModulePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Air = 3
}

public sealed class ModuleExclusivityPolicy
{
    public bool IsExclusive { get; set; } = true;
    public InterruptionBehavior Behavior { get; set; } = InterruptionBehavior.Stop;
    public ModulePriority Priority { get; set; } = ModulePriority.Normal;
}

public sealed class BusExclusivityConfig
{
    public int Version { get; set; } = 1;
    public Dictionary<ModuleRole, ModuleExclusivityPolicy> Policies { get; set; } = new();

    public ModuleExclusivityPolicy GetPolicy(ModuleRole role)
    {
        if (!Policies.TryGetValue(role, out var p))
        {
            p = new ModuleExclusivityPolicy();
            Policies[role] = p;
        }
        return p;
    }
}

public sealed class BusAcquireResult
{
    public bool Granted { get; init; }
    public bool Denied { get; init; }
    public string? ExpropriatedOwnerId { get; init; }
    public ModuleRole ExpropriatedRole { get; init; }
    public InterruptionBehavior ExpropriatedBehavior { get; init; }

    public static readonly BusAcquireResult GrantedNoConflict = new() { Granted = true };
    public static readonly BusAcquireResult DeniedResult = new() { Denied = true };
}

public sealed class ChannelExpropriatedEventArgs : EventArgs
{
    public string BusId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public ModuleRole Role { get; init; }
    /// <summary>
    /// Como debe reaccionar el expropiado. <c>Stop</c>: fade-out + dispose.
    /// <c>Pause</c>: fade-out + pause (espera reanudacion manual).
    /// <c>AutoResume</c>: fade-out + pause; el coordinator lo va a reanudar
    /// cuando el expropiador libere el bus.
    /// </summary>
    public InterruptionBehavior Behavior { get; init; }
}

public sealed class ChannelResumedEventArgs : EventArgs
{
    public string BusId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public ModuleRole Role { get; init; }
}