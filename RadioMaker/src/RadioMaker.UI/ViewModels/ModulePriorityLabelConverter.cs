using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.ViewModels;

public sealed class ModulePriorityLabelConverter : IValueConverter
{
    public static readonly ModulePriorityLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ModulePriority p)
        {
            return p switch
            {
                ModulePriority.Low => "Baja",
                ModulePriority.Normal => "Normal",
                ModulePriority.High => "Alta",
                ModulePriority.Air => "AIRE",
                _ => p.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}