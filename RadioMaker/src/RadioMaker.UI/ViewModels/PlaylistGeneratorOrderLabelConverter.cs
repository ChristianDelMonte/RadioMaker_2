using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Convierte un PlaylistGeneratorOrder a su etiqueta legible en español.
/// </summary>
public sealed class PlaylistGeneratorOrderLabelConverter : IValueConverter
{
    public static readonly PlaylistGeneratorOrderLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlaylistGeneratorOrder order)
        {
            return order switch
            {
                PlaylistGeneratorOrder.Random => "Aleatorio",
                PlaylistGeneratorOrder.AlphabeticalByName => "Alfabético por nombre",
                PlaylistGeneratorOrder.AlphabeticalByPath => "Alfabético por ruta",
                _ => order.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}