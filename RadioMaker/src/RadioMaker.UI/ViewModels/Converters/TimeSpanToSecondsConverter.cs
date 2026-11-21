using System.Globalization;
using Avalonia.Data.Converters;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un <see cref="TimeSpan"/> a segundos (double) para usar en Sliders/ProgressBars.
/// -------------
public sealed class TimeSpanToSecondsConverter : IValueConverter
{
    public static readonly TimeSpanToSecondsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TimeSpan ts ? ts.TotalSeconds : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
}
