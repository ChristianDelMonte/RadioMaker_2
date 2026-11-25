using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un PlaylistGeneratorOrder a su etiqueta legible en español.
/// -------------
public sealed class PlaylistGeneratorOrderLabelConverter : IValueConverter
{
    public static readonly PlaylistGeneratorOrderLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlaylistGeneratorOrder order)
        {
            return order switch
            {
                PlaylistGeneratorOrder.Random => LocalizationService.GetString("Loc.Label.GeneratorOrder.Random"),
                PlaylistGeneratorOrder.AlphabeticalByName => LocalizationService.GetString("Loc.Label.GeneratorOrder.Name"),
                PlaylistGeneratorOrder.AlphabeticalByPath => LocalizationService.GetString("Loc.Label.GeneratorOrder.Path"),
                _ => order.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
