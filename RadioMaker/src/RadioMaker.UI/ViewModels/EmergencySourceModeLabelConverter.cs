using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Configuration;

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
                EmergencySourceMode.Prearmed => "Pre lista (.rmf)",
                EmergencySourceMode.Folder => "Carpeta (shuffle)",
                EmergencySourceMode.Hybrid => "Hibrido (pre lista + carpeta)",
                _ => m.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}