using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Converter que toma un booleano y devuelve un brush resuelto desde los
/// recursos de tema, eligiendo entre dos claves segun el valor.
///
/// Uso en XAML:
///     Foreground="{Binding IsActive, Converter={x:Static vm:BoolToThemeBrushConverter.Instance}, ConverterParameter='LedActive|LedInactive'}"
///
/// El parametro tiene el formato "ClaveTrue|ClaveFalse". Si solo se pasa
/// una clave, se usa para ambos estados.
///
/// Tambien soporta colores hex crudos como fallback ("#00E5FF|#0D3D4A").
/// -------------
public sealed class BoolToThemeBrushConverter : IValueConverter
{
    public static readonly BoolToThemeBrushConverter Instance = new();

    private static readonly IBrush Transparent = new SolidColorBrush(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isTrue = value is bool b && b;

        if (parameter is not string param || string.IsNullOrWhiteSpace(param))
            return Transparent;

        // Parsear "ClaveTrue|ClaveFalse".
        var parts = param.Split('|');
        string key = isTrue
            ? parts[0]
            : (parts.Length > 1 ? parts[1] : parts[0]);

        if (string.IsNullOrWhiteSpace(key))
            return Transparent;

        var app = Application.Current;
        if (app is not null)
        {
            // IMPORTANTE: usar el ActualThemeVariant explicito. La version
            // con "null" no resuelve claves dentro de ThemeDictionaries.
            var variant = app.ActualThemeVariant;

            if (variant is not null &&
                app.TryGetResource(key, variant, out var resource) &&
                resource is not null)
            {
                if (resource is IBrush brush) return brush;
                if (resource is Color color) return new SolidColorBrush(color);
            }

            // Fallback sin variant (por si la clave esta fuera de los diccionarios).
            if (app.TryGetResource(key, null, out resource) && resource is not null)
            {
                if (resource is IBrush brush2) return brush2;
                if (resource is Color color2) return new SolidColorBrush(color2);
            }
        }

        // Fallback final: parsear como color hex crudo.
        try
        {
            var parsed = Color.Parse(key);
            return new SolidColorBrush(parsed);
        }
        catch
        {
            return Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}