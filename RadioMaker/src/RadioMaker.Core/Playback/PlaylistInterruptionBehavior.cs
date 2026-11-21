namespace RadioMaker.Core.Playback;

/// -------------
/// Comportamiento del PlaylistPlayer cuando un programado lo interrumpe.
/// -------------
public enum PlaylistInterruptionBehavior
{
    /// -------------Pausar y reanudar desde donde quedo (default).</summary>
    Continue = 0,

    /// -------------Detener y al terminar el programado saltar al siguiente item.</summary>
    SkipToNext = 1
}