using RadioMaker.Core.Scheduling;

namespace RadioMaker.Core.Abstractions;

/// -------------
/// Estado de reproduccion del motor programado.
/// -------------
public enum PlaybackStateKind
{
    Idle = 0,
    Playing = 1,
    Suspended = 2,
    Error = 3
}

/// -------------
/// Snapshot del estado de reproduccion del PlaybackEngine.
/// La UI lo usa para mostrar que esta sonando ahora.
/// Si el item es una Tanda, los campos Tanda* describen la pista actual.
/// -------------
public sealed record PlaybackStateInfo(
    PlaybackStateKind Kind,
    string ItemTitle,
    string SourceListName,
    SchedulePriority Priority,
    double PositionSeconds,
    double DurationSeconds,
    string? ErrorMessage,

    // ───── Info de la tanda (vacia si el item es SingleFile) ─────────────
    string TandaName = "",
    int TandaTrackIndex = -1,      // 0-based. -1 = no es tanda.
    int TandaTrackCount = 0,
    string TandaTrackTitle = "")
{
    /// -------------Estado inicial (nada reproduciendose).</summary>
    public static readonly PlaybackStateInfo Idle = new(
        PlaybackStateKind.Idle,
        string.Empty,
        string.Empty,
        SchedulePriority.Low,
        0,
        0,
        null);

    // ───── Propiedades de presentacion ───────────────────────────────────

    public string KindText => Kind switch
    {
        PlaybackStateKind.Idle => "Inactivo",
        PlaybackStateKind.Playing => "Reproduciendo",
        PlaybackStateKind.Suspended => "Suspendido",
        PlaybackStateKind.Error => "Error",
        _ => "?"
    };

    public string KindColor => Kind switch
    {
        PlaybackStateKind.Idle => "FgMuted",
        PlaybackStateKind.Playing => "Success",
        PlaybackStateKind.Suspended => "Warning",
        PlaybackStateKind.Error => "Error",
        _ => "FgMuted"
    };

    public string PriorityText => Priority == SchedulePriority.High ? "ALTA" : "Baja";
    public string PriorityColor => Priority == SchedulePriority.High ? "Warning" : "FgMuted";

    public bool IsActive => Kind == PlaybackStateKind.Playing || Kind == PlaybackStateKind.Suspended;
    public bool HasItem => !string.IsNullOrWhiteSpace(ItemTitle);
    public bool HasSource => !string.IsNullOrWhiteSpace(SourceListName);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// -------------True si el item que suena es una Tanda.</summary>
    public bool IsTanda => TandaTrackIndex >= 0;

    /// -------------Texto de progreso: "Pista 3 de 8".</summary>
    public string TandaProgressText => IsTanda
        ? $"Pista {TandaTrackIndex + 1} de {TandaTrackCount}"
        : string.Empty;

    /// -------------Nombre de la tanda para mostrar (vacio si no es tanda).</summary>
    public string TandaNameText => IsTanda ? TandaName : string.Empty;

    /// -------------Posicion / duracion en formato mm:ss.</summary>
    public string PositionText
    {
        get
        {
            var pos = TimeSpan.FromSeconds(Math.Max(0, PositionSeconds));
            var dur = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            return $"{(int)pos.TotalMinutes:D2}:{pos.Seconds:D2} / {(int)dur.TotalMinutes:D2}:{dur.Seconds:D2}";
        }
    }

    /// -------------Progreso 0..1 para bindear a ProgressBar.</summary>
    public double ProgressFraction =>
        DurationSeconds > 0.1 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0.0;
}