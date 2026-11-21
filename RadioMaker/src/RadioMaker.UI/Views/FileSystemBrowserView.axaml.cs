using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;
using System.Threading.Tasks;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind del File Manager.
/// -------------
public partial class FileSystemBrowserView : UserControl
{
    public FileSystemBrowserView()
    {
        InitializeComponent();
    }

    private FileSystemBrowserViewModel? GetVm() => DataContext as FileSystemBrowserViewModel;

    /// -------------
    /// Nodo del arbol que el usuario clickeo con el boton derecho por
    /// ultima vez. Se usa desde los handlers del ContextMenu para saber
    /// sobre que carpeta operar.
    /// -------------
    private FileSystemNodeViewModel? _lastClickedNode;

    // ─────────────────────────────────────────────────────────────────────
    // Doble clic / Enter = activar archivo (agregar a la pestaña activa).

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        GetVm()?.ActivateSelectedFileCommand.Execute(null);
    }

    private void OnFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GetVm()?.ActivateSelectedFileCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Doble clic en un nodo del arbol = expandir + navegar.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedNode is null) return;

        vm.SelectedNode.IsExpanded = !vm.SelectedNode.IsExpanded;
        _ = vm.NavigateToAsync(vm.SelectedNode.FullPath);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Iniciar drag & drop desde un archivo de la lista.
    private async void OnFilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not AudioFileViewModel fileVm) return;

        var point = e.GetCurrentPoint(this);

        // Click derecho: seleccionar el item para que el menu contextual
        // (que abre justo despues) opere sobre el archivo correcto.
        if (point.Properties.IsRightButtonPressed)
        {
            if (GetVm() is { } vm)
                vm.SelectedFile = fileVm;
            return;
        }

        // Solo con boton izquierdo a partir de aca.
        if (!point.Properties.IsLeftButtonPressed) return;

        try
        {
            // Avalonia 12: DataTransfer con items.
            // El path viaja como texto plano.
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(fileVm.FullPath));

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Menu contextual: editar tags
    private void OnFileContextEditTags(object? sender, RoutedEventArgs e)
    {
        var fileVm = GetVm()?.SelectedFile;
        if (fileVm is null) return;

        _ = OpenTagEditorAsync(fileVm);
    }

    private void OnFileContextEditInRMWave(object? sender, RoutedEventArgs e)
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
        // Asegurar que los tags esten cargados antes de abrir el editor.
        if (!fileVm.TagsLoaded)
            await fileVm.LoadTagsAsync();

        var editorVm = new AudioTagEditorViewModel(fileVm);
        var win = new AudioTagEditorWindow { DataContext = editorVm };

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        // ShowDialog devuelve true si guardo, false si cancelo. En ambos
        // casos el VM ya se encargo de recargar los tags del archivo si
        // hubo cambios, asi que la lista y el panel inferior se actualizan solos.
        await win.ShowDialog<bool>(owner);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Context menu del arbol: agregar / quitar favoritos

    private void OnTreeNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not FileSystemNodeViewModel node) return;

        // Solo capturamos el nodo cuando es click derecho.
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        // Re-evaluar el estado de favorito al momento del click derecho.
        // Esto cubre el caso de un nodo que se expandio DESPUES del ultimo
        // cambio del registry (sus hijos no recibieron la notificacion).
        var registry = GetVm()?.FavoriteFolders;
        if (registry is not null)
        {
            try { node.IsFavorite = registry.IsFavorite(node.FullPath); }
            catch { }
        }

        _lastClickedNode = node;
    }

    private async void OnAddFavoriteClick(object? sender, RoutedEventArgs e)
    {
        var node = _lastClickedNode;
        if (node is null) return;

        var registry = GetVm()?.FavoriteFolders;
        if (registry is null) return;

        // Si ya es favorito, no hacer nada (no deberia llegar aca por el IsEnabled).
        if (registry.IsFavorite(node.FullPath)) return;

        // Pedir nombre personalizado.
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialogVm = new AddFavoriteDialogViewModel(node.FullPath);
        var dialog = new AddFavoriteDialog { DataContext = dialogVm };

        var ok = await dialog.ShowDialog<bool>(owner);
        if (!ok) return;

        // Agregar al registry. El nombre personalizado puede estar vacio
        // (en cuyo caso se usa el nombre de la carpeta).
        var entry = registry.Add(node.FullPath, dialogVm.CustomName);

        if (entry is not null)
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = $"Agregado a favoritos: {entry.EffectiveName}";
        }
        else
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = "No se pudo agregar a favoritos.";
        }
    }

    private void OnRemoveFavoriteClick(object? sender, RoutedEventArgs e)
    {
        var node = _lastClickedNode;
        if (node is null) return;

        var registry = GetVm()?.FavoriteFolders;
        if (registry is null) return;

        bool removed = registry.Remove(node.FullPath);

        var vm = GetVm();
        if (vm is not null)
        {
            vm.StatusMessage = removed
                ? $"Quitado de favoritos: {node.DisplayName}"
                : "No se pudo quitar de favoritos.";
        }
    }

}