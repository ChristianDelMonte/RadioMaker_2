namespace RadioMaker.Core.Playback;

/// -------------
/// Comandos de locución soportados por RMVoice desde RadioMaker.
/// Cada valor mapea a un <c>RMVoice.Engine.AnnounceContent</c>.
///
/// Uso:
///   - Como mixado intermedio de una pista (suena a los X segundos).
///   - Como item de playlist (próxima fase).
/// -------------
public enum LocutionCommand
{
    /// -------------Hora + minutos ("14:35").</summary>
    HourAndMinute = 0,

    /// -------------Temperatura + humedad ("22 grados, 65 por ciento").</summary>
    Climate = 1,

    /// -------------Hora + minutos + temperatura + humedad.</summary>
    HourAndClimate = 2
}