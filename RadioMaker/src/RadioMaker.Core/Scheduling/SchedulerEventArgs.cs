namespace RadioMaker.Core.Scheduling;

/// -------------
/// Argumentos del evento ItemTriggered que emite el SchedulerService
/// cuando detecta que un ScheduledItem debe ejecutarse.
///
/// El consumidor (PlaybackEngine en Sprint 6.5.3) usa estos datos para
/// decidir como reproducir: como SingleFile, como Tanda, con que prioridad.
/// -------------
public sealed class SchedulerEventArgs : EventArgs
{
    /// -------------El item que debe ejecutarse.</summary>
    public ScheduledItem Item { get; init; } = new();

    /// -------------Nombre de la lista de la que proviene (para logs).</summary>
    public string SourceListName { get; init; } = string.Empty;

    /// -------------Momento exacto en que el scheduler detecto el disparo (hora local).</summary>
    public DateTime FiredAt { get; init; } = DateTime.Now;

    public override string ToString() =>
        $"[{FiredAt:HH:mm:ss}] {SourceListName} → {Item.DisplayTitle} ({Item.Priority})";
}