namespace RMVoice.Core.Models;

/// -------------
/// Ajustes globales de la app (independientes del perfil de voz).
/// Se serializan en %AppData%/RMVoice/settings.json.
/// -------------
public sealed class AppSettings
{
    /// -------------Ubicación activa para las consultas de clima.
    public WeatherLocation? ActiveWeatherLocation { get; set; }

    /// -------------¿Refrescar automáticamente el clima cada X minutos?
    public bool AutoWeatherRefresh { get; set; }

    /// -------------Intervalo de refresco automático en minutos (mínimo 1).
    public int WeatherRefreshMinutes { get; set; } = 15;

    /// -------------¿Anunciar por voz después de cada actualización de clima?
    public bool AnnounceAfterWeatherFetch { get; set; }

    // ───── Último snapshot de clima (persistido para sobrevivir al cierre) ─────

    /// -------------Temperatura (°C) del último snapshot obtenido de Open-Meteo.
    public double? LastTemperature { get; set; }

    /// -------------Humedad (%) del último snapshot obtenido de Open-Meteo
    public double? LastHumidity { get; set; }

    /// -------------Momento en que se obtuvo el último snapshot.
    public DateTime? LastWeatherTimestamp { get; set; }
}