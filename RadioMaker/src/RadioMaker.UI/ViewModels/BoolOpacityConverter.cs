using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Convierte un booleano a un valor de opacidad para bindear en XAML.
/// true  => 1.0 (opaco)
/// false => 0.5 (semi-transparente)
/// </summary>
public sealed class BoolOpacityConverter : IValueConverter
{
    public static readonly BoolOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.5;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}