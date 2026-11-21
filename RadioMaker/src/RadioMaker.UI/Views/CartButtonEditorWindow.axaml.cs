using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class CartButtonEditorWindow : CustomChromeWindow
{
    private readonly FilePickerFileType _audioFileType;

    public CartButtonEditorWindow()
        : this(null)
    {
    }

    public CartButtonEditorWindow(IEnumerable<string>? supportedAudioExtensions)
    {
        InitializeComponent();
        _audioFileType = AudioFilePickerHelper.CreateAudioFileType(supportedAudioExtensions);
    }

    private CartButtonEditorViewModel? GetVm() => DataContext as CartButtonEditorViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Botones inferiores

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null)
        {
            Close(false);
            return;
        }

        // Aplicar los cambios al boton original.
        vm.ApplyToTarget();

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Examinar archivo

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar archivo para el boton",
            AllowMultiple = false,
            FileTypeFilter = new[] { _audioFileType, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.FilePath = path;

        // Sugerir el nombre del archivo si la etiqueta esta vacia.
        if (string.IsNullOrWhiteSpace(vm.Label))
            vm.Label = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private void OnClearFileClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.ClearFile();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Color rapido

    private void OnQuickColorClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not string color) return;

        vm.SetQuickColor(color);
    }
}
