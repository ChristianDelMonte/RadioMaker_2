namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Nivel de severidad de una entrada de log del scheduler.
/// </summary>
public enum SchedulerLogLevel
{
    /// <summary>Mensaje informativo (ej: scheduler iniciado).</summary>
    Info = 0,

    /// <summary>Un item programado se disparo.</summary>
    Trigger = 1,

    /// <summary>Advertencia (ej: item con archivo inexistente).</summary>
    Warning = 2,

    /// <summary>Error (ej: no se pudo reproducir).</summary>
    Error = 3
}

/// <summary>
/// Entrada de log del scheduler. Se muestra en el panel de programacion
/// para que el operador vea que esta pasando en tiempo real.
/// </summary>
public sealed record SchedulerLogEntry(
    DateTime Timestamp,
    SchedulerLogLevel Level,
    string Message)
{
    /// <summary>Hora formateada como hh:mm:ss para mostrar.</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>Color asociado al nivel, para pintar en la UI.</summary>
    public string LevelColor => Level switch
    {
        SchedulerLogLevel.Info => "FgSecondary",
        SchedulerLogLevel.Trigger => "Success",
        SchedulerLogLevel.Warning => "Warning",
        SchedulerLogLevel.Error => "Error",
        _ => "FgSecondary"
    };

    /// <summary>Etiqueta corta del nivel.</summary>
    public string LevelText => Level switch
    {
        SchedulerLogLevel.Info => "INFO",
        SchedulerLogLevel.Trigger => "FIRE",
        SchedulerLogLevel.Warning => "WARN",
        SchedulerLogLevel.Error => "ERR",
        _ => "?"
    };
}