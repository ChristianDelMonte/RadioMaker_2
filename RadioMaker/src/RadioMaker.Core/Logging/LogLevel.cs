namespace RadioMaker.Core.Logging;

/// <summary>
/// Nivel de severidad de una entrada de log de la aplicacion.
/// </summary>
public enum LogLevel
{
    /// <summary>Informacion de depuracion (verboso, util en desarrollo).</summary>
    Debug = 0,

    /// <summary>Evento normal de la aplicacion.</summary>
    Info = 1,

    /// <summary>Situacion anomala pero recuperable.</summary>
    Warning = 2,

    /// <summary>Error que afecta una operacion puntual.</summary>
    Error = 3
}