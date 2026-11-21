namespace RMVoice.Core.Models;

/// -------------
/// Lectura puntual de clima: temperatura y humedad actuales.
/// -------------
public sealed class WeatherSnapshot
{
    public DateTime Timestamp { get; set; }

    /// -------------Temperatura en grados Celsius (Open-Meteo devuelve °C por defecto).
    public double Temperature { get; set; }

    /// -------------Humedad relativa en porcentaje (0–100).
    public double Humidity { get; set; }
}
