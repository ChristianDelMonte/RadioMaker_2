using System.Collections.ObjectModel;
using RadioMaker.UI.Services;
using RadioMaker.UI.Localization;
using Avalonia.Media.Imaging;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Info de una libreria externa usada por la app.
/// -------------
public sealed class DependencyInfo
{
    public string Name { get; }
    public string Version { get; }
    public string License { get; }
    public string Description { get; }
    public string Url { get; }

    /// -------------Color clave de recurso para el badge de licencia.</summary>
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
            var l when l.Contains("Gratis", StringComparison.OrdinalIgnoreCase) => "Accent",
            var l when l.Contains("Comercial", StringComparison.OrdinalIgnoreCase) => "Error",
            _ => "FgMuted"
        };
    }
}

/// -------------
/// Grupo de dependencias por categoria.
/// -------------
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

/// -------------
/// Link rapido que se muestra en el tab "Acerca de".
/// -------------
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

/// -------------
/// ViewModel de la ventana "Acerca de RadioMaker".
///
/// Expone:
///   - Info de la app (nombre, version, build, slogan, copyright).
///   - Lista de dependencias externas agrupadas por categoria, con
///     version, licencia, descripcion y link a la web del proyecto.
///   - Comandos para abrir links en el navegador externo.
/// -------------
public sealed partial class AboutWindowViewModel : ViewModelBase, IDisposable
{
    // ───── Info de la app ────────────────────────────────────────────────

    public string AppName => "RadioMaker";
    public string AppVersion => Services.AppVersionInfo.VersionText;
    public string AppBuild => Services.AppVersionInfo.BuildText;
    public string AppSlogan => LocalizationService.GetString("Loc.App.Slogan");
    public string AppDescription => LocalizationService.GetString("Loc.About.Description");

    public string CopyrightText => string.Format(LocalizationService.GetString("Loc.About.Copyright"), DateTime.Now.Year);

    /// -------------
    /// Imagen del header. Es la misma que se mostro en el splash screen
    /// al arrancar la app (se elige al azar una vez por sesion).
    ///
    /// Es un Bitmap (no un string) porque Avalonia no convierte strings
    /// a Bitmap automaticamente cuando vienen por Binding.
    /// -------------
    public Bitmap? SplashImage => SplashImageProvider.CurrentBitmap;

    public string FrameworkText => Services.AppVersionInfo.PlatformText;

    // ───── Dependencias ──────────────────────────────────────────────────

    public ObservableCollection<DependencyGroup> DependencyGroups { get; } = new();

    // ───── Constructor ───────────────────────────────────────────────────

    public AboutWindowViewModel()
    {
        LocalizationService.LanguageChanged += OnLanguageChanged;
        BuildDependencyGroups();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DependencyGroups.Clear();
        BuildDependencyGroups();
        OnPropertyChanged(nameof(DependencyGroups));
        OnPropertyChanged(nameof(AppVersion));
        OnPropertyChanged(nameof(AppBuild));
        OnPropertyChanged(nameof(AppSlogan));
        OnPropertyChanged(nameof(AppDescription));
        OnPropertyChanged(nameof(CopyrightText));
        OnPropertyChanged(nameof(FrameworkText));
        OnPropertyChanged(nameof(QuickLinks));
    }

    public void Dispose() => LocalizationService.LanguageChanged -= OnLanguageChanged;

    private void BuildDependencyGroups()
    {
        string DependencyDescription(string name) => LocalizationService.GetString($"Loc.About.Dependency.{name}");

        // ── Framework / UI ───────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            LocalizationService.GetString("Loc.About.Group.Framework"),
            "\uE30A", // icono generico (no se usa, placeholder)
            new List<DependencyInfo>
            {
                new("Avalonia", "12.1.3", "MIT",
                    DependencyDescription("Avalonia"),
                    "https://avaloniaui.net"),

                new("Avalonia.Desktop", "12.1.3", "MIT",
                    DependencyDescription("AvaloniaDesktop"),
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("Avalonia.Themes.Fluent", "12.1.3", "MIT",
                    DependencyDescription("Fluent"),
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("Avalonia.Fonts.Inter", "12.1.3", "MIT",
                    DependencyDescription("Inter"),
                    "https://github.com/AvaloniaUI/Avalonia"),

                new("AvaloniaUI.DiagnosticsSupport", "2.2.3", "MIT",
                    DependencyDescription("Diagnostics"),
                    "https://github.com/AvaloniaUI/Avalonia"),
            }));

        // ── MVVM / Utilidades ────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            LocalizationService.GetString("Loc.About.Group.Mvvm"),
            "",
            new List<DependencyInfo>
            {
                new("CommunityToolkit.Mvvm", "8.4.2", "MIT",
                    DependencyDescription("Mvvm"),
                    "https://github.com/CommunityToolkit/dotnet"),

                new("Material.Icons.Avalonia", "3.0.2", "MIT",
                    DependencyDescription("MaterialIcons"),
                    "https://github.com/SKProCH/Material.Icons"),
            }));

        // ── Audio ────────────────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            LocalizationService.GetString("Loc.About.Group.Audio"),
            "",
            new List<DependencyInfo>
            {
                new("BASS", "2.4.x", LocalizationService.GetString("Loc.About.License.Bass"),
                    DependencyDescription("Bass"),
                    "https://www.un4seen.com"),

                new("ManagedBass", "4.0.2", "MIT",
                    DependencyDescription("ManagedBass"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Fx", "4.0.2", "MIT",
                    DependencyDescription("BassFx"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Flac", "4.0.2", "MIT",
                    DependencyDescription("Flac"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Aac", "4.0.2", "MIT",
                    DependencyDescription("Aac"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Opus", "4.0.2", "MIT",
                    DependencyDescription("Opus"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("ManagedBass.Wv", "4.0.2", "MIT",
                    DependencyDescription("WavPack"),
                    "https://github.com/ManagedBass/ManagedBass"),

                new("TagLib#", "2.3.0", "LGPL-2.1 / MIT",
                    DependencyDescription("TagLib"),
                    "https://github.com/mono/taglib-sharp"),
            }));

        // ── Reportes ─────────────────────────────────────────────────────
        DependencyGroups.Add(new DependencyGroup(
            LocalizationService.GetString("Loc.About.Group.Reports"),
            "",
            new List<DependencyInfo>
            {
                new("ClosedXML", "0.105.1", "MIT",
                    DependencyDescription("ClosedXml"),
                    "https://github.com/ClosedXML/ClosedXML"),

                new("QuestPDF", "2024.12.3", LocalizationService.GetString("Loc.About.License.QuestPdf"),
                    DependencyDescription("QuestPdf"),
                    "https://www.questpdf.com"),
            }));
    }

    // ───── Links ─────────────────────────────────────────────────────────

    /// -------------
    /// Links rapidos que aparecen en el pie del tab "Acerca de".
    /// -------------
    public IReadOnlyList<QuickLinkInfo> QuickLinks => new List<QuickLinkInfo>
    {
        new(LocalizationService.GetString("Loc.About.Link.Avalonia"), "https://avaloniaui.net"),
        new(LocalizationService.GetString("Loc.About.Link.Bass"), "https://www.un4seen.com"),
        new(LocalizationService.GetString("Loc.About.Link.DotNet"), "https://dotnet.microsoft.com/download/dotnet/8.0"),
    };
}
