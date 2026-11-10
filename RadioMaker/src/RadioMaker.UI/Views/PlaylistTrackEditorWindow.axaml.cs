using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RadioMaker.UI.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class PlaylistTrackEditorWindow : CustomChromeWindow
{
    public PlaylistTrackEditorWindow()
    {
        InitializeComponent();
    }

    private PlaylistTrackEditorViewModel? GetVm() => DataContext as PlaylistTrackEditorViewModel;

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
    // Archivo principal

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingTrack is null) return;

        var path = await PickAudioFileAsync("Seleccionar archivo de audio");
        if (string.IsNullOrEmpty(path)) return;

        vm.WorkingTrack.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.WorkingTrack.Title))
            vm.WorkingTrack.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Mixado

    private async void OnBrowseMixClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingTrack is null) return;

        var path = await PickAudioFileAsync("Seleccionar archivo de mixado");
        if (string.IsNullOrEmpty(path)) return;

        vm.WorkingTrack.MixAudioPath = path;
    }

    private void OnClearMixClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.WorkingTrack is null) return;

        vm.WorkingTrack.MixAudioPath = string.Empty;
        vm.WorkingTrack.MixMode = TrackMixMode.None;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helper

    private async System.Threading.Tasks.Task<string?> PickAudioFileAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var audioTypes = new FilePickerFileType("Archivos de audio")
        {
            Patterns = new[]
            {
                "*.mp3", "*.ogg", "*.wav", "*.flac", "*.m4a", "*.aac", "*.opus", "*.wv"
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { audioTypes, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }
}