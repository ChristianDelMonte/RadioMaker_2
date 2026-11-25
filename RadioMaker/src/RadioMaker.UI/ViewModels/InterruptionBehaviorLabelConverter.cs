using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.Core.Playback;
using RadioMaker.UI.Localization;

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
                PlaylistInterruptionBehavior.Continue => LocalizationService.GetString("Loc.Settings.Body.BehaviorContinue"),
                PlaylistInterruptionBehavior.SkipToNext => LocalizationService.GetString("Loc.Settings.Body.BehaviorSkipShort"),
                _ => b.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
