using System.Collections.ObjectModel;
using RadioMaker.UI.Services;
using Avalonia.Media.Imaging;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Info de una libreria externa usada por la app.
/// </summary>
public sealed class DependencyInfo
{
    public string Name { get; }
    public string Version { get; }
    public string License { get; }
    public string Description { get; }
    public string Url { get; }

    /// <summary>Color clave de recurso para el badge de licencia.</summary>
    public string LicenseColor { get; }

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    public DependencyInfo(string name, string version, string license, string description, string url)
    {
        Name = name;
        Version = version;
        License = license;
        Description = description;
        Url = url;

        // Color segun tipo de licencia.
        LicenseColor = license switch
        {
            var l when l.Contains("MIT", StringComparison.OrdinalIgnoreCase) => "Success",
            var l when l.Contains("LGPL", StringComparison.OrdinalIgnoreCase) => "Warning",
            var l when l.Contains("GPL", StringComparison.OrdinalIgnoreCase) => "Warning",
            var l when l.Contains("Community", StringComparison.OrdinalIgnoreCase) => "Accent",
            var l when l.Contains("Free", StringComparison.OrdinalIgnoreCase) => "Accent",
            var l when l.Contains("Comercial", StringComparison.OrdinalIgnoreCase) => "Error",
            _ => "FgMuted"
        };
    }
}

/// <summary>
/// Grupo de dependencias por categoria.
/// </summary>
public sealed class DependencyGroup
{
    public string Title { get; }
    public string Icon { get; }
    public List<DependencyInfo> Items { get; }

    public DependencyGroup(string title, string icon, List<DependencyInfo> items)
    {
        Title = title;
        Icon = icon;
        Items = items;
    }
}

/// <summary>
/// Link rapido que se muestra en el tab "Acerca de".
/// </summary>
public sealed class QuickLinkInfo
{
    public string Label { get; }
    public string Url { get; }

    public QuickLinkInfo(string label, string url)
    {
        Label = label;
        Url = url;
    }
}

/// <summary>
/// ViewModel de la ventana "Acerca de RadioMaker".
///
/// Expone:
///   - Info de la app (nombre, version, build, slogan, copyright).
///   - Lista de dependencias externas agrupadas por categoria, con
///     version, licencia, descripcion y link a la web del proyecto.
///   - Comandos para abrir links en el navegador externo.
/// </summary>
public sealed partial class AboutWindowViewModel : ViewModelBase
{
    // ───── Info de la app ────────────────────────────────────────────────

    public string AppName => "RadioMaker";
    public string AppVersion => Services.AppVersionInfo.VersionText;
    public string AppBuild => Services.AppVersionInfo.BuildText;
    public string AppSlogan => "Forever FREE Radio Automation Software!";
    public string AppDescription =>
        "Software de automatizacion de radio multiplataforma. " +
        "Reproduccion programada, botonera, tandas, playlists y analisis de audio " +
        "en un solo entorno profesional.";

    public string CopyrightText { get; }

    /// <summary>
    /// Imagen del header. Es la misma que se mostro en el splash screen
    /// al arrancar la app (se elige al azar una vez por sesion).
    ///
    /// Es un Bitmap (no un string) porque Avalonia no convierte strings
    /// a Bitmap automaticamente cuando vienen por Binding.
    /// </summary>
    public Bitmap? SplashImage => SplashImageProvider.CurrentBitmap;

    public string FrameworkText => Services.AppVersionInfo.PlatformText;

    // ───── Dependencias ──────────────────────────────────────────────────

    public ObservableCollection<DependencyGroup> DependencyGroups { get; } = new();

    // ───── Constructor ───────────────────────────────────────────────────

    public AboutWindowViewModel()
    {
        CopyrightText = $"\u00A9 {DateTime.Now.Year} RadioMaker. Todos los derechos reservados.";

        BuildDependencyGroups();
    }

    private void BuildDependencyGroups()
    {
        // ── Framework / UI ───────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            "Framework y UI",
            "\uE30A", // icono generico (no se usa, placeholder)
            new List<DependencyInfo>
            {
                new("Avalonia", "12.1.3", "MIT",
                    "Framework de UI multiplataforma para .NET.",
                    "https://avaloniaui.net"),

                new("Avalonia.Desktop", "12.1.3", "MIT",
                    "Backend de escritorio para Avalonia.",
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("Avalonia.Themes.Fluent", "12.1.3", "MIT",
                    "Tema Fluent (Windows 11) para Avalonia.",
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("Avalonia.Fonts.Inter", "12.1.3", "MIT",
                    "Tipografia Inter empaquetada para Avalonia.",
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("AvaloniaUI.DiagnosticsSupport", "2.2.3", "MIT",
                    "Herramientas de diagnostico y DevTools para Avalonia.",
                    "https://github.com/AvaloniaUI/Avalonia"),
            }));

        // ── MVVM / Utilidades ────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            "MVVM y utilidades",
            "",
            new List<DependencyInfo>
            {
                new("CommunityToolkit.Mvvm", "8.4.2", "MIT",
                    "Helpers MVVM modernos: ObservableProperty, RelayCommand, etc.",
                    "https://github.com/CommunityToolkit/dotnet"),

                new("Material.Icons.Avalonia", "3.0.2", "MIT",
                    "Iconografia Material Design para Avalonia.",
                    "https://github.com/SKProCH/Material.Icons"),
            }));

        // ── Audio ────────────────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            "Motor de audio",
            "",
            new List<DependencyInfo>
            {
                new("BASS", "2.4.x", "Free (no comercial)",
                    "Motor de audio nativo multiplataforma de Un4seen Developments. " +
                    "Gratis para uso personal/educativo. Uso comercial requiere licencia.",
                    "https://www.un4seen.com"),

                new("ManagedBass", "4.0.2", "MIT",
                    "Wrapper .NET multiplataforma de la libreria BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Fx", "4.0.2", "MIT",
                    "AddOn de efectos de audio (EQ, reverb, echo) para BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Flac", "4.0.2", "MIT",
                    "AddOn de decodificacion FLAC para BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Aac", "4.0.2", "MIT",
                    "AddOn de decodificacion AAC / M4A para BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Opus", "4.0.2", "MIT",
                    "AddOn de decodificacion Opus para BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Wv", "4.0.2", "MIT",
                    "AddOn de decodificacion WavPack para BASS.",
                    "https://github.com/ManagedBass/ManagedBass"),

                new("TagLib#", "2.3.0", "LGPL-2.1 / MIT",
                    "Lectura y escritura de tags de audio (ID3, Vorbis, FLAC, MP4, APE).",
                    "https://github.com/mono/taglib-sharp"),
            }));

        // ── Reportes ─────────────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            "Reportes y exportacion",
            "",
            new List<DependencyInfo>
            {
                new("ClosedXML", "0.105.1", "MIT",
                    "Generacion y manipulacion de archivos Excel (.xlsx).",
                    "https://github.com/ClosedXML/ClosedXML"),

                new("QuestPDF", "2024.12.3", "Community License",
                    "Generacion de documentos PDF. Licencia Community gratuita " +
                    "para proyectos con facturacion anual menor a USD 1M.",
                    "https://www.questpdf.com"),
            }));
    }

    // ───── Links ─────────────────────────────────────────────────────────

    /// <summary>
    /// Links rapidos que aparecen en el pie del tab "Acerca de".
    /// </summary>
    public IReadOnlyList<QuickLinkInfo> QuickLinks { get; } = new List<QuickLinkInfo>
    {
        new("Sitio web de Avalonia", "https://avaloniaui.net"),
        new("BASS (Un4seen)", "https://www.un4seen.com"),
        new(".NET 8", "https://dotnet.microsoft.com/download/dotnet/8.0"),
    };
}