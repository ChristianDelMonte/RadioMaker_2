using Material.Icons;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Wrapper de cada seccion del panel de configuracion unificado.
/// Tiene lo necesario para mostrarse en la barra lateral (titulo + icono)
/// y el VM real que se renderiza en el area de contenido.
/// </summary>
public sealed class SettingsSectionViewModel : ViewModelBase
{
    /// <summary>Clave estable para identificar la seccion programaticamente.</summary>
    public string Key { get; }

    /// <summary>Titulo que se muestra en la barra lateral.</summary>
    public string Title { get; }

    /// <summary>Icono de Material Design para la barra lateral.</summary>
    public MaterialIconKind IconKind { get; }

    /// <summary>VM real que se renderiza en el panel derecho.</summary>
    public object Content { get; }

    public SettingsSectionViewModel(string key, string title, MaterialIconKind iconKind, object content)
    {
        Key = key ?? string.Empty;
        Title = title ?? string.Empty;
        IconKind = iconKind;
        Content = content;
    }
}