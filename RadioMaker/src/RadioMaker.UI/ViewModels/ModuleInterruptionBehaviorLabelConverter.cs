using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Abstractions;

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
                InterruptionBehavior.Stop => "Detener",
                InterruptionBehavior.Pause => "Pausar",
                InterruptionBehavior.AutoResume => "Pausar y reanudar",
                _ => b.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}