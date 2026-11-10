namespace RadioMaker.Core.Playback;

/// <summary>
/// Comandos de locución soportados por RMVoice desde RadioMaker.
/// Cada valor mapea a un <c>RMVoice.Engine.AnnounceContent</c>.
///
/// Uso:
///   - Como mixado intermedio de una pista (suena a los X segundos).
///   - Como item de playlist (próxima fase).
/// </summary>
public enum LocutionCommand
{
    /// <summary>Hora + minutos ("14:35").</summary>
    HourAndMinute = 0,

    /// <summary>Temperatura + humedad ("22 grados, 65 por ciento").</summary>
    Climate = 1,

    /// <summary>Hora + minutos + temperatura + humedad.</summary>
    HourAndClimate = 2
}