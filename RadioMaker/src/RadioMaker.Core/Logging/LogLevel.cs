namespace RadioMaker.Core.Logging;

/// -------------
/// Nivel de severidad de una entrada de log de la aplicacion.
/// -------------
public enum LogLevel
{
    /// -------------Informacion de depuracion (verboso, util en desarrollo).</summary>
    Debug = 0,

    /// -------------Evento normal de la aplicacion.</summary>
    Info = 1,

    /// -------------Situacion anomala pero recuperable.</summary>
    Warning = 2,

    /// -------------Error que afecta una operacion puntual.</summary>
    Error = 3
}