namespace RMVoice.Core.Models;

/// <summary>
/// Lectura puntual de clima: temperatura y humedad actuales.
/// </summary>
public sealed class WeatherSnapshot
{
    public DateTime Timestamp { get; set; }

    /// <summary>Temperatura en grados Celsius (Open-Meteo devuelve °C por defecto).</summary>
    public double Temperature { get; set; }

    /// <summary>Humedad relativa en porcentaje (0–100).</summary>
    public double Humidity { get; set; }
}
