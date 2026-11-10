using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Playback;

namespace RadioMaker.UI.ViewModels;

public sealed class InterruptionBehaviorLabelConverter : IValueConverter
{
    public static readonly InterruptionBehaviorLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlaylistInterruptionBehavior b)
        {
            return b switch
            {
                PlaylistInterruptionBehavior.Continue => "Continuar donde quedo",
                PlaylistInterruptionBehavior.SkipToNext => "Saltar al siguiente",
                _ => b.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}