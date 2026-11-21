namespace RMVoice.Core.Models;

/// -------------
/// Cada una de las pistas de audio que maneja la aplicación.
/// Una pista = un archivo de audio + su tabla de regiones.
/// -------------
public enum TrackKind
{
    Hour,
    Minute,
    Temperature,
    Humidity
}
