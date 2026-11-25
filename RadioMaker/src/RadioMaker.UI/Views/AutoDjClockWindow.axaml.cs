using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Localization;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class AutoDjClockWindow : CustomChromeWindow
{
    public AutoDjClockWindow()
    {
        InitializeComponent();
        Closed += (_, _) => ViewModel?.Dispose();
    }

    private AutoDjClockWindowViewModel? ViewModel => DataContext as AutoDjClockWindowViewModel;

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.AutoDJ.Dialog.SelectMusicFolder"),
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && ViewModel is { } vm) vm.SourceFolder = path;
    }

    private async void OnOpenClockClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.AutoDJ.Dialog.OpenClock"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.AutoDJ.Dialog.ClockFileType")) { Patterns = new[] { "*" + AutoDjClockStore.FileExtension } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) ViewModel?.LoadClock(path);
    }

    private async void OnSaveClockClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;
        if (!string.IsNullOrWhiteSpace(vm.CurrentClockPath))
        {
            vm.SaveClock(vm.CurrentClockPath);
            return;
        }
        await SaveClockAsAsync(vm);
    }

    private async void OnSaveClockAsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await SaveClockAsAsync(vm);
    }

    private async Task SaveClockAsAsync(AutoDjClockWindowViewModel vm)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.AutoDJ.Dialog.SaveClock"),
            SuggestedFileName = LocalizationService.GetString("Loc.AutoDJ.Dialog.ClockFileName") + AutoDjClockStore.FileExtension,
            DefaultExtension = AutoDjClockStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.AutoDJ.Dialog.ClockFileType")) { Patterns = new[] { "*" + AutoDjClockStore.FileExtension } }
            }
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SaveClock(path);
    }

    private async void OnSavePlaylistClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || !vm.HasPreview) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.AutoDJ.Dialog.SaveGeneratedPlaylist"),
            SuggestedFileName = "Playlist AutoDJ.rmf",
            DefaultExtension = PlaylistStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.AutoDJ.Dialog.PlaylistFileType")) { Patterns = new[] { "*" + PlaylistStore.FileExtension } }
            }
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SavePlaylist(path);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelPendingOperation();
        Close();
    }

    private async void OnTakeAirClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        await vm.TakeAirCommand.ExecuteAsync(null);
        if (vm.TakeAirAccepted) Close();
    }
}
