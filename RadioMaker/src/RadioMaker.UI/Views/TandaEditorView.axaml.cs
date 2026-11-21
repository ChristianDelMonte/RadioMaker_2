using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind del editor de tandas. Maneja los dialogos nativos de
/// archivos .tnd y la seleccion del archivo de cada pista.
/// -------------
public partial class TandaEditorView : UserControl
{
    public TandaEditorView()
    {
        InitializeComponent();
    }

    private TandaEditorViewModel? GetVm() => DataContext as TandaEditorViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Nuevo / Abrir / Guardar

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.NewTanda();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType("Tanda")
        {
            Patterns = new[] { "*" + TandaStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir tanda (.tnd)",
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.LoadFromFile(path);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        if (!string.IsNullOrEmpty(vm.LoadedFilePath))
        {
            vm.SaveToFile(vm.LoadedFilePath);
            return;
        }

        await SaveAsAsync(vm);
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        await SaveAsAsync(vm);
    }

    private async System.Threading.Tasks.Task SaveAsAsync(TandaEditorViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.TandaName) + TandaStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar tanda (.tnd)",
            SuggestedFileName = suggestedName,
            DefaultExtension = TandaStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Tanda")
                {
                    Patterns = new[] { "*" + TandaStore.FileExtension }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SaveToFile(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Examinar archivo de una pista

    private async void OnBrowseTrackClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not TandaTrackViewModel trackVm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var audioTypes = AudioFilePickerHelper.CreateAudioFileType(
            AudioFilePickerHelper.GetSupportedExtensions(this));

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar archivo de audio",
            AllowMultiple = false,
            FileTypeFilter = new[] { audioTypes, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        trackVm.FilePath = path;

        if (string.IsNullOrWhiteSpace(trackVm.Title))
            trackVm.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private void OnTrackEditInRMWaveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not TandaTrackViewModel trackVm) return;
        if (string.IsNullOrWhiteSpace(trackVm.FilePath)) return;

        var vm = GetVm();
        if (!RMWaveLauncher.OpenFile(trackVm.FilePath, out var error))
        {
            if (vm is not null)
                vm.StatusMessage = error ?? "No se pudo abrir en RMWave.";
        }
        else
        {
            if (vm is not null)
                vm.StatusMessage = $"Abriendo {System.IO.Path.GetFileName(trackVm.FilePath)} en RMWave...";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Util

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "tanda";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}
