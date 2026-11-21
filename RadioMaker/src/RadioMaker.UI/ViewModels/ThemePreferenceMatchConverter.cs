using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Converter de dos vias para bindear RadioButtons a una propiedad de tipo
/// enum. Compara el valor actual contra el ConverterParameter y devuelve
/// true/false. En sentido inverso, si el valor es true, asigna el parameter
/// (que se parsea al enum correspondiente).
///
/// Uso:
///     IsChecked="{Binding Preference, Converter={x:Static ...}, ConverterParameter=System}"
/// -------------
public sealed class ThemePreferenceMatchConverter : IValueConverter
{
    public static readonly ThemePreferenceMatchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ThemePreference current) return false;
        if (parameter is null) return false;

        if (Enum.TryParse<ThemePreference>(parameter.ToString(), out var target))
            return current == target;

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Solo nos importa cuando el RadioButton PASA a true (checked).
        // Cuando se desmarca (false), no tocamos el valor.
        if (value is not bool b || !b) return Avalonia.Data.BindingOperations.DoNothing;
        if (parameter is null) return Avalonia.Data.BindingOperations.DoNothing;

        if (Enum.TryParse<ThemePreference>(parameter.ToString(), out var target))
            return target;

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}