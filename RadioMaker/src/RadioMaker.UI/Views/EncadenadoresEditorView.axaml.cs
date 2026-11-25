using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class EncadenadoresEditorView : UserControl
{
    public EncadenadoresEditorView() => InitializeComponent();

    private EncadenadoresEditorViewModel? GetVm() => DataContext as EncadenadoresEditorViewModel;

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed &&
            sender is Control { DataContext: EncadenadorTrackViewModel track })
        {
            var vm = GetVm();
            if (vm is not null) vm.SelectedTrack = track;
        }
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is { } track) _ = OpenTrackEditorAsync(vm, track);
    }

    private async void OnContextEditTagsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        var track = vm?.SelectedTrack;
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath)) return;

        if (!File.Exists(track.FilePath))
        {
            vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.Chainers.Status.FileNotFound"), Path.GetFileName(track.FilePath));
            return;
        }

        try
        {
            var fileVm = new AudioFileViewModel(track.FilePath);
            await fileVm.LoadTagsAsync();
            var editorVm = new AudioTagEditorViewModel(fileVm);
            var win = new AudioTagEditorWindow { DataContext = editorVm };
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;

            if (await win.ShowDialog<bool>(owner))
                vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.Chainers.Status.TagsSaved"), Path.GetFileName(track.FilePath));
        }
        catch (Exception ex)
        {
            vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.Chainers.Status.TagsError"), ex.Message);
        }
    }

    private void OnContextEditClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is { } track) _ = OpenTrackEditorAsync(vm, track);
    }

    private async Task OpenTrackEditorAsync(EncadenadoresEditorViewModel vm, EncadenadorTrackViewModel track)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var pathBox = new TextBox { Text = track.FilePath, IsReadOnly = true };
        var titleBox = new TextBox { Text = track.Model.Title, PlaceholderText = LocalizationService.GetString("Loc.Chainers.Dialog.UseFileName") };
        var browseButton = new Button { Content = LocalizationService.GetString("Loc.Chainers.Dialog.Browse"), Margin = new Avalonia.Thickness(0, 5, 0, 0) };
        browseButton.Click += async (_, _) =>
        {
            var audioTypes = AudioFilePickerHelper.CreateAudioFileType(
                AudioFilePickerHelper.GetSupportedExtensions(this));
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationService.GetString("Loc.Chainers.Dialog.SelectAudio"),
                AllowMultiple = false,
                FileTypeFilter = new[] { audioTypes, FilePickerFileTypes.All }
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                pathBox.Text = path;
                if (string.IsNullOrWhiteSpace(titleBox.Text) ||
                    string.Equals(titleBox.Text, Path.GetFileNameWithoutExtension(track.FilePath), StringComparison.OrdinalIgnoreCase))
                    titleBox.Text = Path.GetFileNameWithoutExtension(path);
            }
        };

        var saveButton = new Button { Content = LocalizationService.GetString("Loc.Chainers.Dialog.Save"), MinWidth = 90 };
        var cancelButton = new Button { Content = LocalizationService.GetString("Loc.Chainers.Dialog.Cancel"), MinWidth = 90 };
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 14, 0, 0),
            Children = { cancelButton, saveButton }
        };
        var content = new StackPanel
        {
            Spacing = 6,
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = LocalizationService.GetString("Loc.Chainers.Dialog.AudioFile") }, pathBox, browseButton,
                new TextBlock { Text = LocalizationService.GetString("Loc.Chainers.Dialog.OptionalTitle"), Margin = new Avalonia.Thickness(0, 8, 0, 0) },
                titleBox, actions
            }
        };
        var dialog = new EncadenadorTrackEditorWindow
        {
            Title = LocalizationService.GetString("Loc.Chainers.Dialog.EditAudio"),
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MinWidth = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        layout.Children.Add(new ModalTitleBar
        {
            WindowTitle = dialog.Title,
            IsVisible = dialog.IsCustomChromeEnabled
        });
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        dialog.Content = layout;
        saveButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        if (await dialog.ShowDialog<bool>(owner))
        {
            var path = pathBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                vm.UpdateTrack(track, path, titleBox.Text ?? string.Empty);
                vm.StatusMessage = string.Format(LocalizationService.GetString("Loc.Chainers.Status.AudioUpdated"), track.DisplayTitle);
            }
        }
    }

    private void OnNewClick(object? sender, RoutedEventArgs e) => GetVm()?.NewSetCommand.Execute(null);

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        var topLevel = TopLevel.GetTopLevel(this);
        if (vm is null || topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.Chainers.Dialog.Open"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.Chainers.Dialog.FileType"))
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
            Title = LocalizationService.GetString("Loc.Chainers.Dialog.SaveAs"),
            SuggestedFileName = vm.Name + EncadenadoresStore.FileExtension,
            DefaultExtension = EncadenadoresStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.Chainers.Dialog.FileType"))
                {
                    Patterns = new[] { "*" + EncadenadoresStore.FileExtension }
                }
            }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SaveToFile(path);
    }

    private sealed class EncadenadorTrackEditorWindow : CustomChromeWindow
    {
    }
}
