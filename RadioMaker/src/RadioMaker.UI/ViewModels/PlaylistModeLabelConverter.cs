using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------Convierte un PlaylistMode a su etiqueta en español.</summary>
public sealed class PlaylistModeLabelConverter : IValueConverter
{
    public static readonly PlaylistModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlaylistMode m)
        {
            return m switch
            {
                PlaylistMode.Once => LocalizationService.GetString("Loc.Label.PlaylistMode.Once"),
                PlaylistMode.Loop => LocalizationService.GetString("Loc.Label.PlaylistMode.Loop"),
                PlaylistMode.Shuffle => LocalizationService.GetString("Loc.Label.PlaylistMode.Shuffle"),
                _ => m.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
