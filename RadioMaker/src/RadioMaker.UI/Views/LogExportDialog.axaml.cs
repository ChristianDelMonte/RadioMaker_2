using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

/// -------------
/// Dialogo modal de exportacion de log.
///
/// Uso:
///     var vm = new LogExportDialogViewModel(allEntries, filteredEntries);
///     var win = new LogExportDialog { DataContext = vm };
///     var ok = await win.ShowDialog<bool>(owner);
///     // si ok == true, la exportacion se completo con exito.
/// -------------
public partial class LogExportDialog : CustomChromeWindow
{
    public LogExportDialog()
    {
        InitializeComponent();
    }

    private LogExportDialogViewModel? GetVm() => DataContext as LogExportDialogViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Botones

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) { Close(false); return; }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        // Nombre sugerido con extension.
        var baseName = string.IsNullOrWhiteSpace(vm.FileName)
            ? "rms-log"
            : SanitizeFileName(vm.FileName);

        var suggestedName = baseName + LogExporter.GetExtension(vm.Format);

        // Dialogo nativo de guardar.
        var fileType = new FilePickerFileType(LogExporter.GetDisplayName(vm.Format))
        {
            Patterns = new[] { LogExporter.GetFilePickerPattern(vm.Format) }
        };

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.Dialog.SaveLog.Title"),
            SuggestedFileName = suggestedName,
            DefaultExtension = vm.Extension,
            FileTypeChoices = new[] { fileType, FilePickerFileTypes.All }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        // Exportar.
        var entries = vm.EntriesToExport;
        var success = LogExporter.Export(entries, path, vm.Format);

        if (!success)
        {
            // TODO: mostrar mensaje de error al usuario. Por ahora, log silencioso.
            // Se puede agregar un MessageBox o un Dialog mas adelante.
            System.Diagnostics.Debug.WriteLine($"Fallo al exportar log a '{path}'");
            return;
        }

        Close(true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "rms-log";

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}
