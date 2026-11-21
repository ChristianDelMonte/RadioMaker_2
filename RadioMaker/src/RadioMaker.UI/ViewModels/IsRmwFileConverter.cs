using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Devuelve true si el valor (string) es una ruta .rmw.
/// Uso típico:
///   IsEnabled="{Binding FilePath, Converter={x:Static vm:IsRmwFileConverter.Instance}}"
///   IsVisible="{Binding FilePath, Converter={x:Static vm:IsRmwFileConverter.Instance}}"
///
/// Acepta null/vacío → false (la opción queda deshabilitada).
/// Es case-insensitive en la extensión.
/// -------------
public sealed class IsRmwFileConverter : IValueConverter
{
    public static readonly IsRmwFileConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return s.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();

    /// -------------Versión inversa: útil para "Editar tags" (ocultar/deshabilitar si ES .rmw).</summary>
    public static bool IsNotRmw(string? path)
        => !( !string.IsNullOrWhiteSpace(path)
              && path.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase) );
}