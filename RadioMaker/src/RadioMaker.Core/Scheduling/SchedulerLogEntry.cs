namespace RadioMaker.Core.Scheduling;

/// -------------
/// Nivel de severidad de una entrada de log del scheduler.
/// -------------
public enum SchedulerLogLevel
{
    /// -------------Mensaje informativo (ej: scheduler iniciado).</summary>
    Info = 0,

    /// -------------Un item programado se disparo.</summary>
    Trigger = 1,

    /// -------------Advertencia (ej: item con archivo inexistente).</summary>
    Warning = 2,

    /// -------------Error (ej: no se pudo reproducir).</summary>
    Error = 3
}

/// -------------
/// Entrada de log del scheduler. Se muestra en el panel de programacion
/// para que el operador vea que esta pasando en tiempo real.
/// -------------
public sealed record SchedulerLogEntry(
    DateTime Timestamp,
    SchedulerLogLevel Level,
    string Message)
{
    /// -------------Hora formateada como hh:mm:ss para mostrar.</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// -------------Color asociado al nivel, para pintar en la UI.</summary>
    public string LevelColor => Level switch
    {
        SchedulerLogLevel.Info => "FgSecondary",
        SchedulerLogLevel.Trigger => "Success",
        SchedulerLogLevel.Warning => "Warning",
        SchedulerLogLevel.Error => "Error",
        _ => "FgSecondary"
    };

    /// -------------Etiqueta corta del nivel.</summary>
    public string LevelText => Level switch
    {
        SchedulerLogLevel.Info => "INFO",
        SchedulerLogLevel.Trigger => "FIRE",
        SchedulerLogLevel.Warning => "WARN",
        SchedulerLogLevel.Error => "ERR",
        _ => "?"
    };
}