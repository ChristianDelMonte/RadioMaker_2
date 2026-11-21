using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.UI.Controls;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un SpectrumMode a su etiqueta legible en español para mostrar
/// en el ComboBox del panel Spectrum.
/// -------------
public sealed class SpectrumModeLabelConverter : IValueConverter
{
    public static readonly SpectrumModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SpectrumMode mode)
        {
            return mode switch
            {
                SpectrumMode.Fft => "FFT",
                SpectrumMode.LogBands => "Bandas",
                SpectrumMode.Waterfall => "Waterfall",
                SpectrumMode.Waveform => "Onda",
                _ => mode.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}