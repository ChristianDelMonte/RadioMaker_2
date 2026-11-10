using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RadioMaker.UI.Controls;
using RadioMaker.UI.ViewModels;
using System.Diagnostics;
using System.Text;

namespace RadioMaker.UI.Views;

/// <summary>
/// Ventana "Acerca de RadioMaker".
///
/// Contiene dos tabs:
///   - "Acerca de": info general, version, build, links rapidos.
///   - "Librerias": dependencias externas con licencia y URL.
///
/// Los links se abren en el navegador externo del sistema.
/// </summary>
public partial class AboutWindow : CustomChromeWindow
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Apertura de links

    private void OnQuickLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.Tag is not string url) return;
        OpenUrl(url);
    }

    private void OnDependencyLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.Tag is not string url) return;
        if (string.IsNullOrWhiteSpace(url)) return;
        OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort: si falla, no rompemos la ventana.
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Copiar info al portapapeles
    // ─────────────────────────────────────────────────────────────────────
    private async void OnCopyInfoClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is null) return;

            var vm = DataContext as AboutWindowViewModel;
            if (vm is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"{vm.AppName} — {vm.AppVersion}");
            sb.AppendLine(vm.AppBuild);
            sb.AppendLine(vm.AppSlogan);
            sb.AppendLine();
            sb.AppendLine(vm.AppDescription);
            sb.AppendLine();
            sb.AppendLine("── Plataforma ─────────────────────────");
            sb.AppendLine(vm.FrameworkText);
            sb.AppendLine();
            sb.AppendLine("── Librerías utilizadas ───────────────");
            foreach (var group in vm.DependencyGroups)
            {
                sb.AppendLine();
                sb.AppendLine($"[{group.Title}]");
                foreach (var d in group.Items)
                {
                    sb.AppendLine($"  • {d.Name} v{d.Version} — {d.License}");
                    if (!string.IsNullOrWhiteSpace(d.Url))
                        sb.AppendLine($"    {d.Url}");
                }
            }
            sb.AppendLine();
            sb.AppendLine(vm.CopyrightText);

            // Avalonia 12: se usa DataTransfer (mismo patron que drag & drop).
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(sb.ToString()));
            await topLevel.Clipboard.SetDataAsync(transfer);
        }
        catch
        {
            // Best effort.
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cerrar

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}