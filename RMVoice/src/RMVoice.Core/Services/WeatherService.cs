using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using RMVoice.Core.Models;

namespace RMVoice.Core.Services;

/// <summary>
/// Cliente de la API pública de Open-Meteo (no requiere clave):
///
///   • Geocodificación: https://geocoding-api.open-meteo.com/v1/search
///   • Clima actual:    https://api.open-meteo.com/v1/forecast
///                      ?current=temperature_2m,relative_humidity_2m
/// </summary>
public sealed class WeatherService : IDisposable
{
    private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";

    private readonly HttpClient _http;
    private bool _disposed;

    public WeatherService(string userAgent = "RMVoice.Engine/1.0")
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var ua = string.IsNullOrWhiteSpace(userAgent) ? "RMVoice.Engine/1.0" : userAgent;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>Busca ubicaciones por nombre. Devuelve lista vacía si no hay coincidencias.</summary>
    public async Task<List<WeatherLocation>> SearchAsync(string query, CancellationToken ct = default)
    {
        var result = new List<WeatherLocation>();

        if (string.IsNullOrWhiteSpace(query))
            return result;

        var url = $"{GeocodingUrl}?name={Uri.EscapeDataString(query.Trim())}" +
                  "&count=10&language=es&format=json";

        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return result;

        foreach (var item in results.EnumerateArray())
        {
            result.Add(new WeatherLocation
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Country = item.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                Admin1 = item.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "",
                Latitude = item.TryGetProperty("latitude", out var lat) ? lat.GetDouble() : 0,
                Longitude = item.TryGetProperty("longitude", out var lon) ? lon.GetDouble() : 0
            });
        }

        return result;
    }

    /// <summary>Temperatura (°C) y humedad (%) actuales para la ubicación indicada.</summary>
    public async Task<WeatherSnapshot> FetchCurrentAsync(WeatherLocation location, CancellationToken ct = default)
    {
        var lat = location.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString("0.######", CultureInfo.InvariantCulture);

        var url = $"{ForecastUrl}?latitude={lat}&longitude={lon}" +
                  "&current=temperature_2m,relative_humidity_2m";

        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("current", out var current))
            throw new InvalidOperationException("La respuesta de Open-Meteo no contiene 'current'.");

        var temp = current.TryGetProperty("temperature_2m", out var t) ? t.GetDouble() : 0;
        var hum = current.TryGetProperty("relative_humidity_2m", out var h) ? h.GetDouble() : 0;

        return new WeatherSnapshot
        {
            Timestamp = DateTime.Now,
            Temperature = temp,
            Humidity = hum
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}