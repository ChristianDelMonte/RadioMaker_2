namespace RMVoice.Core.Models;

/// <summary>
/// Cada una de las pistas de audio que maneja la aplicación.
/// Una pista = un archivo de audio + su tabla de regiones.
/// </summary>
public enum TrackKind
{
    Hour,
    Minute,
    Temperature,
    Humidity
}
