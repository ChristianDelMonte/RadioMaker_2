using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Configuration;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

public sealed class EmergencySourceModeLabelConverter : IValueConverter
{
    public static readonly EmergencySourceModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EmergencySourceMode m)
        {
            return m switch
            {
                EmergencySourceMode.Prearmed => LocalizationService.GetString("Loc.Label.EmergencySource.Prearmed"),
                EmergencySourceMode.Folder => LocalizationService.GetString("Loc.Label.EmergencySource.Folder"),
                EmergencySourceMode.Hybrid => LocalizationService.GetString("Loc.Label.EmergencySource.Hybrid"),
                _ => m.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
