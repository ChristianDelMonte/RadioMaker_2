using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Inverso de IsRmwFileConverter: true si el valor NO es .rmw.
/// Uso típico: IsEnabled="{Binding FilePath, Converter={x:Static vm:IsNotRmwFileConverter.Instance}}"
/// </summary>
public sealed class IsNotRmwFileConverter : IValueConverter
{
    public static readonly IsNotRmwFileConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return !s.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}