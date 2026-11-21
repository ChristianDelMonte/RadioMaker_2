using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Playback;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un <see cref="LocutionCommand"/> a su etiqueta legible
/// para el ComboBox del editor de pista.
/// -------------
public sealed class LocutionCommandLabelConverter : IValueConverter
{
    public static readonly LocutionCommandLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LocutionCommand cmd)
        {
            return cmd switch
            {
                LocutionCommand.HourAndMinute => "Hora + minutos",
                LocutionCommand.Climate => "Temperatura y humedad",
                LocutionCommand.HourAndClimate => "Hora + temperatura y humedad",
                _ => cmd.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}