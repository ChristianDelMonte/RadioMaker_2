namespace RadioMaker.Core.Abstractions;

/// -------------
/// Estado de reproducción de un canal, independiente del backend.
/// -------------
public enum PlaybackState
{
    Stopped = 0,
    Playing,
    Paused,
    Stalled,
    Ended,
    Error
}
