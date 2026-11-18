using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class ScheduledListView : UserControl
{
    private readonly DispatcherTimer _scheduleRefreshTimer;

    public ScheduledListView()
    {
        InitializeComponent();

        _scheduleRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _scheduleRefreshTimer.Tick += (_, _) => RefreshScheduleDisplay();
        AttachedToVisualTree += (_, _) =>
        {
            RefreshScheduleDisplay();
            _scheduleRefreshTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => _scheduleRefreshTimer.Stop();

        AddHandler(DragDrop.DragOverEvent, OnDragOverAny);
        AddHandler(DragDrop.DropEvent, OnDropAny);
        DragDrop.SetAllowDrop(this, true);
    }

    private ScheduledListViewModel? GetVm() => DataContext as ScheduledListViewModel;

    private void RefreshScheduleDisplay()
    {
        if (GetVm() is not { } vm) return;
        foreach (var item in vm.Items)
            item.RefreshScheduleDisplay();
    }

    private Window? GetOwnerWindow() => TopLevel.GetTopLevel(this) as Window;

    // ─────────────────────────────────────────────────────────────────────
    // Drag & drop

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
        if (vm is null) return;

        vm.AddItemFromPath(path);
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
    // Seleccion con click derecho

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not ScheduledItemViewModel itemVm) return;
        if (GetVm() is not { } vm) return;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            vm.SelectedItem = itemVm;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Doble clic → editar

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedItem is null) return;

        _ = OpenEditorAsync(vm.SelectedItem);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Context menu

    private void OnContextEditClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var item = vm.SelectedItem;
        if (item is null) return;

        _ = OpenEditorAsync(item);
    }

    private void OnContextEditInRMWaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var item = vm.SelectedItem;
        if (item is null) return;

        if (item.Kind != ScheduledItemKind.SingleFile) return;
        if (string.IsNullOrWhiteSpace(item.FilePath)) return;

        if (!RMWaveLauncher.OpenFile(item.FilePath, out var error))
            vm.StatusMessage = error ?? "No se pudo abrir en RMWave.";
        else
            vm.StatusMessage = $"Abriendo {System.IO.Path.GetFileName(item.FilePath)} en RMWave...";
    }

    private void OnContextDeleteClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        vm.RemoveSelectedItemCommand.Execute(null);
    }

    private async System.Threading.Tasks.Task OpenEditorAsync(ScheduledItemViewModel itemVm)
    {
        var owner = GetOwnerWindow();
        if (owner is null) return;

        var editorVm = new ScheduledItemEditorViewModel(itemVm);
        var win = new ScheduledItemEditorWindow { DataContext = editorVm };

        var result = await win.ShowDialog<bool>(owner);

        if (result)
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = $"Item actualizado: {itemVm.DisplayTitle}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Nuevo / Abrir / Guardar

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.NewList();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType("Lista de programados")
        {
            Patterns = new[] { "*" + ScheduledListStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir lista de programados",
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

    private async System.Threading.Tasks.Task SaveAsAsync(ScheduledListViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.ListName) + ScheduledListStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar lista de programados",
            SuggestedFileName = suggestedName,
            DefaultExtension = ScheduledListStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Lista de programados")
                {
                    Patterns = new[] { "*" + ScheduledListStore.FileExtension }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SaveToFile(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Util

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "sin_nombre";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}
