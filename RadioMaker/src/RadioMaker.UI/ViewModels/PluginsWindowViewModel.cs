using System.Collections.ObjectModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel de la ventana "Plugins de formato".
/// Muestra el estado de cada addon cargado y los formatos soportados
/// agrupados por origen (nativo BASS + cada plugin cargado).
/// -------------
public sealed class PluginsWindowViewModel : ViewModelBase
{
    public ObservableCollection<PluginInfoViewModel> Plugins { get; } = new();
    public ObservableCollection<FormatGroupViewModel> FormatGroups { get; } = new();

    public string SummaryText { get; }
    public bool HasAnyPlugin { get; }
    public bool HasNoPlugins => !HasAnyPlugin;

    public PluginsWindowViewModel(IAudioEngine engine)
    {
        foreach (var plugin in engine.LoadedPlugins)
            Plugins.Add(new PluginInfoViewModel(plugin));

        BuildFormatGroups(engine);

        int loaded = Plugins.Count(p => p.IsLoaded);
        int total = Plugins.Count;

        HasAnyPlugin = loaded > 0;
        SummaryText = string.Format(LocalizationService.GetString("Loc.Plugins.Summary"), loaded, total);
    }

    /// -------------Constructor sin engine para el diseñador de XAML.</summary>
    public PluginsWindowViewModel()
    {
        SummaryText = LocalizationService.GetString("Loc.Plugins.EmptySummary");
        HasAnyPlugin = false;
    }

    private void BuildFormatGroups(IAudioEngine engine)
    {
        // Extensiones aportadas por plugins que cargaron OK.
        var pluginExts = new HashSet<string>(
            engine.LoadedPlugins
                  .Where(p => p.IsLoaded)
                  .SelectMany(p => p.Extensions),
            StringComparer.OrdinalIgnoreCase);

        // Extensiones nativas = las que soporta el engine menos las que
        // aporta un plugin (asi no se duplican).
        var nativeExts = engine.SupportedExtensions
            .Where(e => !pluginExts.Contains(e))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (nativeExts.Count > 0)
            FormatGroups.Add(new FormatGroupViewModel(LocalizationService.GetString("Loc.Plugins.NativeBass"), nativeExts));

        // Un grupo por cada plugin cargado con exito.
        foreach (var plugin in engine.LoadedPlugins
                                    .Where(p => p.IsLoaded && p.Extensions.Count > 0)
                                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            FormatGroups.Add(new FormatGroupViewModel(plugin.Name, plugin.Extensions));
        }
    }
}

/// -------------
/// Representa una linea del panel "Formatos soportados actualmente":
/// el nombre del origen (nativo o plugin) y las extensiones que aporta.
/// -------------
public sealed class FormatGroupViewModel
{
    public string Name { get; }
    public string ExtensionsText { get; }

    public FormatGroupViewModel(string name, IReadOnlyList<string> extensions)
    {
        Name = name;
        ExtensionsText = extensions.Count > 0
            ? "(" + string.Join("; ", extensions.Select(e => "*" + e)) + ")"
            : LocalizationService.GetString("Loc.Plugins.NoExtensions");
    }
}
