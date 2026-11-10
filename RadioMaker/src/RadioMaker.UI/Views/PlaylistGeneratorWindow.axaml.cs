using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class PlaylistGeneratorWindow : CustomChromeWindow
{
    public PlaylistGeneratorWindow()
    {
        InitializeComponent();
    }

    private PlaylistGeneratorWindowViewModel? GetVm()
        => DataContext as PlaylistGeneratorWindowViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Carpeta de origen

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar carpeta de origen",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SourceFolder = path;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Presets

    private async void OnOpenPresetClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType("Preset del generador")
        {
            Patterns = new[] { "*" + PlaylistGeneratorPresetStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir preset (.rpg)",
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.LoadPresetFromPath(path);
    }

    private void OnSavePresetClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        if (!vm.HasCurrentPreset) return;

        vm.SaveCurrentPreset();
    }

    private async void OnSavePresetAsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        // Nombre sugerido: el del preset actual, o uno generico.
        var suggested = string.IsNullOrWhiteSpace(vm.CurrentPresetName)
            ? "Preset Generador"
            : vm.CurrentPresetName;

        var suggestedFileName = SanitizeFileName(suggested)
            + PlaylistGeneratorPresetStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar preset (.rpg)",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = PlaylistGeneratorPresetStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Preset del generador")
                {
                    Patterns = new[] { "*" + PlaylistGeneratorPresetStore.FileExtension }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        // El nombre del preset = nombre del archivo sin extension.
        var presetName = Path.GetFileNameWithoutExtension(path);

        vm.SavePresetAs(path, presetName);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Modo

    private void OnTrackCountModeClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        vm.Mode = PlaylistGeneratorMode.TrackCount;
    }

    private void OnDurationModeClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        vm.Mode = PlaylistGeneratorMode.TotalDuration;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Aceptar / Cancelar

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.CancelPendingOperation();
        Close(false);
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null || !vm.HasPreview) { Close(false); return; }

        Close(true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Util

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "preset";

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}