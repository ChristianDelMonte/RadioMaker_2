namespace RadioMaker.Core.Playback;

/// <summary>
/// Estado en el que puede estar un PlaylistPlayer.
/// </summary>
public enum PlaylistPlayerStatus
{
    /// <summary>Sin nada reproduciendo.</summary>
    Stopped = 0,

    /// <summary>Reproduciendo una pista de la playlist.</summary>
    Playing = 1,

    /// <summary>Pausa manual (el usuario apreto Pause).</summary>
    Paused = 2,

    /// <summary>Pausa automatica por un programado del scheduler.</summary>
    PausedByScheduler = 3,

    /// <summary>Esperando el delay entre pistas (de una pista con delay).</summary>
    Delaying = 4,

    /// <summary>Error durante la reproduccion.</summary>
    Error = 5
}

/// <summary>
/// Snapshot del estado de reproduccion de una playlist.
/// La UI lo usa para mostrar el estado actual, la pista en curso, progreso, etc.
/// </summary>
public sealed record PlaylistPlayerState(
    PlaylistPlayerStatus Status,
    string PlaylistName,
    int TrackIndex,       // -1 si no hay pista activa
    int TrackCount,
    string TrackTitle,
    double PositionSeconds,
    double DurationSeconds,
    string? ErrorMessage)
{
    /// <summary>Estado inicial (nada reproduciendo).</summary>
    public static readonly PlaylistPlayerState Stopped = new(
        PlaylistPlayerStatus.Stopped,
        string.Empty,
        -1,
        0,
        string.Empty,
        0,
        0,
        null);

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string StatusText => Status switch
    {
        PlaylistPlayerStatus.Stopped => "Detenido",
        PlaylistPlayerStatus.Playing => "Reproduciendo",
        PlaylistPlayerStatus.Paused => "Pausado",
        PlaylistPlayerStatus.PausedByScheduler => "Pausado (programado)",
        PlaylistPlayerStatus.Delaying => "Esperando",
        PlaylistPlayerStatus.Error => "Error",
        _ => "?"
    };

    public string StatusColor => Status switch
    {
        PlaylistPlayerStatus.Stopped => "FgMuted",
        PlaylistPlayerStatus.Playing => "Success",
        PlaylistPlayerStatus.Paused => "Warning",
        PlaylistPlayerStatus.PausedByScheduler => "Accent",
        PlaylistPlayerStatus.Delaying => "TrackMixColor",
        PlaylistPlayerStatus.Error => "Error",
        _ => "FgMuted"
    };

    public bool IsActive =>
        Status == PlaylistPlayerStatus.Playing ||
        Status == PlaylistPlayerStatus.Paused ||
        Status == PlaylistPlayerStatus.PausedByScheduler ||
        Status == PlaylistPlayerStatus.Delaying;

    public bool HasTrack => TrackIndex >= 0 && !string.IsNullOrWhiteSpace(TrackTitle);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string TrackProgressText => TrackIndex >= 0
        ? $"Pista {TrackIndex + 1} de {TrackCount}"
        : string.Empty;

    public string PositionText
    {
        get
        {
            var pos = TimeSpan.FromSeconds(Math.Max(0, PositionSeconds));
            var dur = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            return $"{(int)pos.TotalMinutes:D2}:{pos.Seconds:D2} / {(int)dur.TotalMinutes:D2}:{dur.Seconds:D2}";
        }
    }

    public double ProgressFraction =>
        DurationSeconds > 0.1 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0.0;
}