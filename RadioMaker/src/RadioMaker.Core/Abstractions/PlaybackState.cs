namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Estado de reproducción de un canal, independiente del backend.
/// </summary>
public enum PlaybackState
{
    Stopped = 0,
    Playing,
    Paused,
    Stalled,
    Ended,
    Error
}
