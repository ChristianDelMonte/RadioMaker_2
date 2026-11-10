using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>Convierte un PlaylistMode a su etiqueta en español.</summary>
public sealed class PlaylistModeLabelConverter : IValueConverter
{
    public static readonly PlaylistModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlaylistMode m)
        {
            return m switch
            {
                PlaylistMode.Once => "Una vez",
                PlaylistMode.Loop => "Repetir",
                PlaylistMode.Shuffle => "Aleatorio",
                _ => m.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}