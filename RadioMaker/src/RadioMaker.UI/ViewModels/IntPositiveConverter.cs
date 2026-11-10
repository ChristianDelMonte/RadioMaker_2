using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

public sealed class IntPositiveConverter : IValueConverter
{
    public static readonly IntPositiveConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}