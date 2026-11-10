namespace RadioMaker.Core.Playback;

/// <summary>
/// Comportamiento del PlaylistPlayer cuando un programado lo interrumpe.
/// </summary>
public enum PlaylistInterruptionBehavior
{
    /// <summary>Pausar y reanudar desde donde quedo (default).</summary>
    Continue = 0,

    /// <summary>Detener y al terminar el programado saltar al siguiente item.</summary>
    SkipToNext = 1
}