using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

public sealed class BoolSchedulerStateTextConverter : IValueConverter
{
    public static readonly BoolSchedulerStateTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "ACTIVO" : "DETENIDO";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}