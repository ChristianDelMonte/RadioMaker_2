using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Localization;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Services;

/// <summary>Construye el filtro compartido de formatos de audio de RadioMaker.</summary>
public static class AudioFilePickerHelper
{
    private static readonly string[] FallbackExtensions =
    {
        ".rmw", ".mp3", ".mp2", ".mp1", ".ogg", ".wav", ".aif", ".aiff",
        ".mo3", ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
        ".flac", ".fla", ".oga", ".m4a", ".m4b", ".aac", ".mp4", ".opus",
        ".wv", ".alac", ".caf", ".ape", ".mpc", ".mp+", ".mpp", ".spx",
        ".ac3", ".dsf", ".dff", ".webm", ".weba", ".wma", ".asf", ".mid", ".midi", ".m3u8"
    };

    public static IReadOnlyCollection<string> GetSupportedExtensions(Control control)
    {
        var mainWindow = TopLevel.GetTopLevel(control) as Window;
        return mainWindow?.DataContext is MainWindowViewModel vm
            ? vm.SupportedExtensions
            : FallbackExtensions;
    }

    public static FilePickerFileType CreateAudioFileType(IEnumerable<string>? supportedExtensions)
    {
        var patterns = (supportedExtensions ?? Array.Empty<string>())
            .Concat(FallbackExtensions)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ext => "*" + ext)
            .ToArray();

        return new FilePickerFileType(LocalizationService.GetString("Loc.Dialog.AudioFileType"))
        {
            Patterns = patterns
        };
    }
}
