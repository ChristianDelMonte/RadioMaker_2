using System.Globalization;

namespace RMVoice.Core.Services;

/// <summary>
/// Conversión entre el formato "HH:MM:SS.f" de las tablas de regiones y segundos (double).
/// </summary>
public static class TimeFormat
{
    public static double Parse(string text)
    {
        if (!TryParse(text, out var seconds))
            throw new FormatException($"Tiempo inválido: '{text}'. Se esperaba HH:MM:SS.f");
        return seconds;
    }

    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':');
        if (parts.Length != 3)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return false;

        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    public static string ToTimeString(double seconds)
    {
        if (seconds < 0) seconds = 0;

        var hours = (int)(seconds / 3600);
        var minutes = (int)(seconds % 3600 / 60);
        var secs = seconds % 60;

        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.0#}", hours, minutes, secs);
    }

    public static string ToTimeStringPrecise(double seconds)
    {
        if (seconds < 0) seconds = 0;

        var hours = (int)(seconds / 3600);
        var minutes = (int)(seconds % 3600 / 60);
        var secs = seconds % 60;

        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.000}", hours, minutes, secs);
    }
}
