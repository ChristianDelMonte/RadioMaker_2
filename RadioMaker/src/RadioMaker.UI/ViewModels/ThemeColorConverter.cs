using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Converter que toma el valor devuelto por las propiedades de color de los
/// VMs y lo traduce a un IBrush del tema actual.
///
/// Acepta dos formas:
///   1. Clave semantica (ej: "Success", "Warning", "Error", "FgMuted",
///      "BgSelected", "RowPlayingBg", ...). Se resuelve contra los recursos
///      de Application.Current.
///   2. Color hex crudo (ej: "#FF0000", "#80FFFFFF"). Se parsea directo.
///      Se usa para colores elegidos por el usuario (colores de botones de
///      la botonera) que NO deben cambiar con el tema.
///
/// Si la clave no se encuentra y no es un hex valido, devuelve un brush
/// transparente para no romper el render.
/// -------------
public sealed class ThemeColorConverter : IValueConverter
{
    public static readonly ThemeColorConverter Instance = new();

    private static readonly IBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return TransparentBrush;

        var app = Application.Current;
        if (app is not null)
        {
            // Variante 1: resolver con el ThemeVariant activo (busca en
            // los ThemeDictionaries del diccionario mergeado).
            var variant = app.ActualThemeVariant;
            if (variant is not null &&
                app.TryGetResource(key, variant, out var resource) &&
                resource is not null)
            {
                if (resource is IBrush brush) return brush;
                if (resource is Color color) return new SolidColorBrush(color);
            }

            // Variante 2: sin variant (busca en recursos globales por si la
            // clave estuviera fuera de los ThemeDictionaries).
            if (app.TryGetResource(key, null, out resource) && resource is not null)
            {
                if (resource is IBrush brush) return brush;
                if (resource is Color color) return new SolidColorBrush(color);
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
            return TransparentBrush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}