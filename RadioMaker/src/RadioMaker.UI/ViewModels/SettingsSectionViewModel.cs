using Material.Icons;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Wrapper de cada seccion del panel de configuracion unificado.
/// Tiene lo necesario para mostrarse en la barra lateral (titulo + icono)
/// y el VM real que se renderiza en el area de contenido.
/// -------------
public sealed class SettingsSectionViewModel : ViewModelBase
{
    /// -------------Clave estable para identificar la seccion programaticamente.</summary>
    public string Key { get; }

    /// -------------Titulo que se muestra en la barra lateral.</summary>
    public string Title { get; }

    /// -------------Icono de Material Design para la barra lateral.</summary>
    public MaterialIconKind IconKind { get; }

    /// -------------VM real que se renderiza en el panel derecho.</summary>
    public object Content { get; }

    public SettingsSectionViewModel(string key, string title, MaterialIconKind iconKind, object content)
    {
        Key = key ?? string.Empty;
        Title = title ?? string.Empty;
        IconKind = iconKind;
        Content = content;
    }
}