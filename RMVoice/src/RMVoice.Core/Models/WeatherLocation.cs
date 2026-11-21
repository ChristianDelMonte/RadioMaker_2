using System.Globalization;

namespace RMVoice.Core.Models;

/// -------------
/// Una ubicación geográfica devuelta por la API de geocodificación de Open-Meteo.
/// Es un DTO serializable en las settings de la app.
/// -------------
public sealed class WeatherLocation
{
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Admin1 { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string DisplayName
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Name))
                parts.Add(Name);

            if (!string.IsNullOrWhiteSpace(Admin1)
                && !Admin1.Equals(Name, StringComparison.OrdinalIgnoreCase))
                parts.Add(Admin1);

            if (!string.IsNullOrWhiteSpace(Country)
                && !Country.Equals(Name, StringComparison.OrdinalIgnoreCase))
                parts.Add(Country);

            return parts.Count == 0 ? "(sin nombre)" : string.Join(", ", parts);
        }
    }

    public string CoordinatesText =>
        $"{Latitude.ToString("0.0000", CultureInfo.InvariantCulture)}, " +
        $"{Longitude.ToString("0.0000", CultureInfo.InvariantCulture)}";

    public override string ToString() => $"{DisplayName}  ({CoordinatesText})";
}
