using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;

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
                ModulePriority.Low => LocalizationService.GetString("Loc.Label.Priority.Low"),
                ModulePriority.Normal => LocalizationService.GetString("Loc.Label.Priority.Normal"),
                ModulePriority.High => LocalizationService.GetString("Loc.Label.Priority.High"),
                ModulePriority.Air => LocalizationService.GetString("Loc.Label.Priority.Air"),
                _ => p.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
