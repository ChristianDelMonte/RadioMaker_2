using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class EncadenadoresEditorView : UserControl
{
    public EncadenadoresEditorView() => InitializeComponent();

    private EncadenadoresEditorViewModel? GetVm() => DataContext as EncadenadoresEditorViewModel;

    private void OnNewClick(object? sender, RoutedEventArgs e) => GetVm()?.NewSetCommand.Execute(null);

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        var topLevel = TopLevel.GetTopLevel(this);
        if (vm is null || topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir Encadenadores (.rme)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Encadenadores .rme")
                {
                    Patterns = new[] { "*" + EncadenadoresStore.FileExtension }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.LoadFromFile(path);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        if (!string.IsNullOrWhiteSpace(vm.LoadedFilePath))
        {
            vm.SaveToFile();
            return;
        }
        await SaveAsAsync(vm);
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is not null) await SaveAsAsync(vm);
    }

    private async Task SaveAsAsync(EncadenadoresEditorViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar Encadenadores (.rme)",
            SuggestedFileName = vm.Name + EncadenadoresStore.FileExtension,
            DefaultExtension = EncadenadoresStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Encadenadores .rme")
                {
                    Patterns = new[] { "*" + EncadenadoresStore.FileExtension }
                }
            }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SaveToFile(path);
    }
}
