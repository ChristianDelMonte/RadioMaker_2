using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Playback;
using RadioMaker.UI.Localization;

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
                LocutionCommand.HourAndMinute => LocalizationService.GetString("Loc.Label.Locution.HourMinute"),
                LocutionCommand.Climate => LocalizationService.GetString("Loc.Label.Locution.Climate"),
                LocutionCommand.HourAndClimate => LocalizationService.GetString("Loc.Label.Locution.HourClimate"),
                _ => cmd.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
