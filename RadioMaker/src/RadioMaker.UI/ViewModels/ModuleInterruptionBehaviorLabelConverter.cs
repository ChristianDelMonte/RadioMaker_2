using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

public sealed class ModuleInterruptionBehaviorLabelConverter : IValueConverter
{
    public static readonly ModuleInterruptionBehaviorLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InterruptionBehavior b)
        {
            return b switch
            {
                InterruptionBehavior.Stop => LocalizationService.GetString("Loc.Label.Interruption.Stop"),
                InterruptionBehavior.Pause => LocalizationService.GetString("Loc.Label.Interruption.Pause"),
                InterruptionBehavior.AutoResume => LocalizationService.GetString("Loc.Label.Interruption.AutoResume"),
                _ => b.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
