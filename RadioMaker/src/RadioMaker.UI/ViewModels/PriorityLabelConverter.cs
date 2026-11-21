using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un SchedulePriority a su etiqueta legible en español.
/// -------------
public sealed class PriorityLabelConverter : IValueConverter
{
    public static readonly PriorityLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SchedulePriority p)
            return p == SchedulePriority.High ? "Alta" : "Baja";
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}