using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

public sealed class BoolSchedulerStateTextConverter : IValueConverter
{
    public static readonly BoolSchedulerStateTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.GetString(value is bool b && b
            ? "Loc.Scheduler.State.Active"
            : "Loc.Scheduler.State.Stopped");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
