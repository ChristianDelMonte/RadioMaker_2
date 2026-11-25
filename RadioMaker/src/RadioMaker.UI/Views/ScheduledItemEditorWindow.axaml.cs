using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class ScheduledItemEditorWindow : CustomChromeWindow
{
    private readonly IReadOnlyCollection<string> _supportedAudioExtensions;

    public ScheduledItemEditorWindow()
        : this(null)
    {
    }

    public ScheduledItemEditorWindow(IEnumerable<string>? supportedAudioExtensions)
    {
        InitializeComponent();
        _supportedAudioExtensions = supportedAudioExtensions?.ToArray() ?? Array.Empty<string>();

        AddHandler(DragDrop.DragOverEvent, OnDragOverAny);
        AddHandler(DragDrop.DropEvent, OnDropAny);
        DragDrop.SetAllowDrop(this, true);
    }

    private ScheduledItemEditorViewModel? GetVm() => DataContext as ScheduledItemEditorViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Drag & drop desde el explorador

    private void OnDragOverAny(object? sender, DragEventArgs e)
    {
        if (HasFileData(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDropAny(object? sender, DragEventArgs e)
    {
        var path = ExtractFirstFilePath(e.DataTransfer);
        if (string.IsNullOrEmpty(path)) return;

        var vm = GetVm();
        if (vm?.WorkingItem is null) return;

        var working = vm.WorkingItem;

        // Si estamos editando un Bloque, agregamos el archivo como item
        // interno del bloque. El tipo se infiere por la extension.
        if (working.Kind == ScheduledItemKind.Block)
        {
            working.AddBlockItemFromPath(path);
            e.Handled = true;
            return;
        }

        // Si estamos en SingleFile / Playlist / Tanda, asignamos el archivo
        // directamente. El metodo se encarga de convertir el tipo si hace falta.
        working.AssignFileFromPath(path);
        e.Handled = true;
    }

    private static bool HasFileData(IDataTransfer? data)
    {
        if (data is null) return false;

        try
        {
            var files = data.TryGetFiles();
            if (files is not null && files.Any()) return true;

            var text = data.TryGetText();
            if (!string.IsNullOrEmpty(text) && File.Exists(text)) return true;
        }
        catch { }

        return false;
    }

    private static string? ExtractFirstFilePath(IDataTransfer? data)
    {
        if (data is null) return null;

        try
        {
            var files = data.TryGetFiles();
            if (files is not null)
            {
                var first = files.FirstOrDefault();
                if (first is not null)
                {
                    var p = first.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(p)) return p;
                }
            }

            var text = data.TryGetText();
            if (!string.IsNullOrEmpty(text) && File.Exists(text))
                return text;
        }
        catch { }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Guardar / Cancelar

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) { Close(false); return; }

        vm.ApplyToOriginal();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cambio de tipo (radio buttons)

    private void OnSingleFileKindClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;
        vm.WorkingItem.ConvertToSingleFileCommand.Execute(null);
    }

    private void OnTandaKindClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;
        vm.WorkingItem.ConvertToTandaCommand.Execute(null);
    }

    private void OnPlaylistKindClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;
        vm.WorkingItem.ConvertToPlaylistCommand.Execute(null);
    }

    private void OnBlockKindClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;
        vm.WorkingItem.ConvertToBlockCommand.Execute(null);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Examinar archivo del item (SingleFile)

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;

        var path = await PickAudioFileAsync(LocalizationService.GetString("Loc.Dialog.SelectAudioFile"));
        if (string.IsNullOrEmpty(path)) return;

        vm.WorkingItem.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.WorkingItem.Title))
            vm.WorkingItem.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Elegir playlist (.rmf)

    private async void OnBrowsePlaylistClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;

        var path = await PickPlaylistAsync();
        if (string.IsNullOrEmpty(path)) return;

        vm.WorkingItem.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.WorkingItem.Title))
            vm.WorkingItem.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Elegir tanda (.tnd)

    private async void OnBrowseTandaClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingItem is null) return;

        var path = await PickTandaAsync();
        if (string.IsNullOrEmpty(path)) return;

        vm.WorkingItem.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.WorkingItem.Title))
            vm.WorkingItem.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Elegir archivo de un item del bloque (segun su Kind)

    private async void OnBrowseBlockItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not ScheduledBlockItemViewModel itemVm) return;

        string? path = itemVm.Kind switch
        {
            ScheduledItemKind.Playlist => await PickPlaylistAsync(),
            ScheduledItemKind.Tanda => await PickTandaAsync(),
            _ => await PickAudioFileAsync(LocalizationService.GetString("Loc.Dialog.SelectAudioFile"))
        };

        if (string.IsNullOrEmpty(path)) return;

        itemVm.FilePath = path;

        if (string.IsNullOrWhiteSpace(itemVm.Title))
        {
            itemVm.Title = System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de picker

    private async System.Threading.Tasks.Task<string?> PickAudioFileAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var audioTypes = AudioFilePickerHelper.CreateAudioFileType(_supportedAudioExtensions);

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { audioTypes, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private async System.Threading.Tasks.Task<string?> PickPlaylistAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var filter = new FilePickerFileType(LocalizationService.GetString("Loc.Dialog.FileType.Playlist"))
        {
            Patterns = new[] { "*" + PlaylistStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.Dialog.SelectPlaylist.Title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private async System.Threading.Tasks.Task<string?> PickTandaAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var filter = new FilePickerFileType(LocalizationService.GetString("Loc.Dialog.FileType.Sequence"))
        {
            Patterns = new[] { "*" + TandaStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.Dialog.SelectSequence.Title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }
}
