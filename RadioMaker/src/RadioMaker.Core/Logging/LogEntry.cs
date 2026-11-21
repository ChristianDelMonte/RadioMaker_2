namespace RadioMaker.Core.Logging;

/// -------------
/// Entrada individual de log de la aplicacion.
/// -------------
public sealed record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Category,
    string Message)
{
    /// -------------Hora formateada como HH:mm:ss.</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// -------------Color asociado al nivel para la UI.</summary>
    public string LevelColor => Level switch
    {
        LogLevel.Debug => "FgMuted",
        LogLevel.Info => "FgSecondary",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        _ => "FgSecondary"
    };

    /// -------------Etiqueta corta del nivel.</summary>
    public string LevelText => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "?"
    };
}