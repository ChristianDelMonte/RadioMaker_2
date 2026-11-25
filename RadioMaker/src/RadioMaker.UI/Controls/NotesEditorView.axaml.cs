using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Localization;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Controls;

/// -------------
/// Code-behind del editor de notas.
///
/// Maneja los dialogos nativos de abrir/guardar archivos .txt. La logica
/// de edicion y autosave vive en el <see cref="NotesEditorViewModel"/>.
/// -------------
public partial class NotesEditorView : UserControl
{
    public NotesEditorView()
    {
        InitializeComponent();
    }

    private NotesEditorViewModel? GetVm() => DataContext as NotesEditorViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Botones

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.NewDocument();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var path = await PickOpenFileAsync();
        if (string.IsNullOrEmpty(path)) return;

        vm.OpenFile(path);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        // Sin archivo asignado -> abrir dialogo de "Guardar como".
        if (!vm.HasFilePath)
        {
            await SaveAsAsync(vm);
            return;
        }

        vm.Save();
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        await SaveAsAsync(vm);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Dialogos nativos

    private async Task<string?> PickOpenFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var txtType = new FilePickerFileType(LocalizationService.GetString("Loc.Dialog.PlainTextFileType"))
        {
            Patterns = new[] { "*.txt" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.Dialog.OpenNote.Title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { txtType, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private async Task SaveAsAsync(NotesEditorViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggested = vm.HasFilePath
            ? Path.GetFileName(vm.LoadedFilePath)
            : "nota.txt";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.Dialog.SaveNote.Title"),
            SuggestedFileName = suggested,
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.Dialog.PlainTextFileType"))
                {
                    Patterns = new[] { "*.txt" }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SaveTo(path);
    }
}
