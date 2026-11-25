using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Wrapper de UI sobre FormatPluginInfo para mostrar el estado del plugin
/// con colores e indicadores visuales.
/// -------------
public sealed class PluginInfoViewModel
{
    public string Name { get; }
    public string ExtensionsText { get; }
    public string VersionText { get; }
    public bool IsLoaded { get; }

    // Propiedades para bindear colores/estado en XAML sin converters.
    public string StatusText => LocalizationService.GetString(IsLoaded ? "Loc.Plugins.Loaded" : "Loc.Plugins.Unavailable");
    public string StatusColor => IsLoaded ? "#10B981" : "#EF4444";
    public string StatusIcon => IsLoaded ? "\u2713" : "\u2717"; // check / cross

    public PluginInfoViewModel(FormatPluginInfo info)
    {
        Name = info.Name;
        IsLoaded = info.IsLoaded;

        ExtensionsText = info.Extensions.Count > 0
            ? string.Join("  ", info.Extensions)
            : LocalizationService.GetString("Loc.Plugins.NoExtensions");

        VersionText = info.IsLoaded && info.Version != new Version(0, 0)
            ? $"v{info.Version}"
            : "—";
    }
}
