namespace RMVoice.Core;

/// -------------
/// Qué piezas se anuncian en una locución.
/// Es un enum de flags para poder componer combinaciones futuras sin romper la API.
/// -------------
[Flags]
public enum AnnounceContent
{
    None = 0,

    Hour        = 1 << 0,
    Minute      = 1 << 1,
    Temperature = 1 << 2,
    Humidity    = 1 << 3,

    // --- Reservados para futuras ampliaciones ---
    // Seconds      = 1 << 4,
    // WeatherLabel = 1 << 5,   // "soleado", "nublado", ...
    // CityName     = 1 << 6,
    // Greeting     = 1 << 7,   // "buenas tardes"

    // --- Atajos de uso habitual ---
    HourOnly            = Hour,
    HourAndMinute       = Hour | Minute,
    ClimateOnly         = Temperature | Humidity,
    HourMinuteAndClimate = Hour | Minute | Temperature | Humidity,
}