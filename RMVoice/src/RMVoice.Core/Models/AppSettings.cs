namespace RMVoice.Core.Models;

/// <summary>
/// Ajustes globales de la app (independientes del perfil de voz).
/// Se serializan en %AppData%/RMVoice/settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Ubicación activa para las consultas de clima.</summary>
    public WeatherLocation? ActiveWeatherLocation { get; set; }

    /// <summary>¿Refrescar automáticamente el clima cada X minutos?</summary>
    public bool AutoWeatherRefresh { get; set; }

    /// <summary>Intervalo de refresco automático en minutos (mínimo 1).</summary>
    public int WeatherRefreshMinutes { get; set; } = 15;

    /// <summary>¿Anunciar por voz después de cada actualización de clima?</summary>
    public bool AnnounceAfterWeatherFetch { get; set; }

    // ───── Último snapshot de clima (persistido para sobrevivir al cierre) ─────

    /// <summary>Temperatura (°C) del último snapshot obtenido de Open-Meteo.</summary>
    public double? LastTemperature { get; set; }

    /// <summary>Humedad (%) del último snapshot obtenido de Open-Meteo.</summary>
    public double? LastHumidity { get; set; }

    /// <summary>Momento en que se obtuvo el último snapshot.</summary>
    public DateTime? LastWeatherTimestamp { get; set; }
}