using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Convierte el booleano HasError del editor de tags a un color:
///   false (sin error) -> verde
///   true  (con error) -> rojo
/// </summary>
public sealed class TagEditorStatusColorConverter : IValueConverter
{
    public static readonly TagEditorStatusColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "#EF4444" : "#10B981";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}