using System.Globalization;
using Avalonia.Data.Converters;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Convierte un LogExportFormat a su etiqueta legible para mostrar en el
/// ComboBox del dialogo de exportacion.
/// -------------
public sealed class LogExportFormatLabelConverter : IValueConverter
{
    public static readonly LogExportFormatLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogExportFormat fmt)
            return LogExporter.GetDisplayName(fmt);

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}