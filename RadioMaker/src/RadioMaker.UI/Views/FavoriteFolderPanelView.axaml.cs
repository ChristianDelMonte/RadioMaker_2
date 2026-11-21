using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Views;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind del panel de carpeta favorita.
///
/// Solo maneja:
///   - Drag&drop de archivos (mismo patron que el explorador).
///   - Doble clic / Enter para activar archivo.
///   - Click derecho -> Editar tags.
/// -------------
public partial class FavoriteFolderPanelView : UserControl
{
    public FavoriteFolderPanelView()
    {
        InitializeComponent();
    }

    private FavoriteFolderPanelViewModel? GetVm() => DataContext as FavoriteFolderPanelViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Drag source: arrastrar archivo hacia otros modulos

    private async void OnFilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not AudioFileViewModel fileVm) return;

        var point = e.GetCurrentPoint(this);

        // Click derecho: solo seleccionar (el ContextMenu hace el resto).
        if (point.Properties.IsRightButtonPressed)
        {
            if (GetVm() is { } vm)
                vm.SelectedFile = fileVm;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(fileVm.FullPath));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Activacion de archivo (doble clic / Enter)

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Doble clic: reproduce el archivo seleccionado en el bus de esta pestaña.
        GetVm()?.ActivateSelectedFile();
    }

    private void OnFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GetVm()?.ActivateSelectedFile();
            e.Handled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Menu contextual: editar tags

    private void OnFileContextEditTags(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var fileVm = GetVm()?.SelectedFile;
        if (fileVm is null) return;

        _ = OpenTagEditorAsync(fileVm);
    }

    private void OnFileContextEditInRMWave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var fileVm = GetVm()?.SelectedFile;
        if (fileVm is null) return;
        if (!RMWaveLauncher.IsRmwFile(fileVm.FullPath)) return;

        var vm = GetVm();
        if (!RMWaveLauncher.OpenFile(fileVm.FullPath, out var error))
        {
            if (vm is not null)
                vm.StatusMessage = error ?? "No se pudo abrir en RMWave.";
        }
        else
        {
            if (vm is not null)
                vm.StatusMessage = $"Abriendo {System.IO.Path.GetFileName(fileVm.FullPath)} en RMWave...";
        }
    }

    private async Task OpenTagEditorAsync(AudioFileViewModel fileVm)
    {
        if (!fileVm.TagsLoaded)
            await fileVm.LoadTagsAsync();

        var editorVm = new AudioTagEditorViewModel(fileVm);
        var win = new AudioTagEditorWindow { DataContext = editorVm };

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        await win.ShowDialog<bool>(owner);
    }
}