using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;  // (si no lo tenés ya)
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Services;
using RadioMaker.UI.Localization;
namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind del editor de playlists. Maneja los dialogos nativos de
/// archivos .rmf, M3U/M3U8 y PLS, mas la seleccion de archivos de audio
/// y mixados de cada pista.
/// -------------
public partial class PlaylistEditorView : UserControl
{
    public PlaylistEditorView()
    {
        InitializeComponent();
    }

    private PlaylistEditorViewModel? GetVm() => DataContext as PlaylistEditorViewModel;

    // ─────────────────────────────────────────────────────────────────────
    // Nuevo / Abrir / Guardar .rmf

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.NewPlaylist();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.FileType"))
        {
            Patterns = new[] { "*" + PlaylistStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.Open"),
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

    // ─────────────────────────────────────────────────────────────────────
    // Seleccion con click derecho + edicion modal

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not PlaylistTrackViewModel trackVm) return;
        if (GetVm() is not { } vm) return;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            vm.SelectedTrack = trackVm;
        }
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is null) return;

        _ = OpenTrackEditorAsync(vm.SelectedTrack);
    }

    private void OnContextEditClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is null) return;

        _ = OpenTrackEditorAsync(vm.SelectedTrack);
    }

    private async void OnContextEditTagsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        var track = vm?.SelectedTrack;
        if (track is null || !track.IsAudioItem || string.IsNullOrWhiteSpace(track.FilePath)) return;

        if (!File.Exists(track.FilePath))
        {
            vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.FileNotFound"), Path.GetFileName(track.FilePath));
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
            vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.TagsSaved"), Path.GetFileName(track.FilePath));
        }
        catch (Exception ex)
        {
            vm!.StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.TagsError"), ex.Message);
        }
    }

    private void OnContextEditInRMWaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        var track = vm?.SelectedTrack;
        if (track is null) return;

        if (!track.IsAudioItem) return;
        if (string.IsNullOrWhiteSpace(track.FilePath)) return;

        if (!RMWaveLauncher.OpenFile(track.FilePath, out var error))
        {
            if (vm is not null)
                vm.StatusMessage = error ?? LocalizationService.GetString("Loc.PlaylistEditor.Status.RmWaveError");
        }
        else
        {
            if (vm is not null)
                vm.StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.OpeningRmWave"), System.IO.Path.GetFileName(track.FilePath));
        }
    }

    private void OnContextDeleteClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        vm.RemoveTrackCommand.Execute(null);
    }

    private async System.Threading.Tasks.Task OpenTrackEditorAsync(PlaylistTrackViewModel trackVm)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var editorVm = new PlaylistTrackEditorViewModel(trackVm);
        var supportedExtensions = (TopLevel.GetTopLevel(this) as Window)?.DataContext is MainWindowViewModel mainVm
            ? mainVm.SupportedExtensions
            : null;
        var win = new PlaylistTrackEditorWindow(supportedExtensions) { DataContext = editorVm };

        var result = await win.ShowDialog<bool>(owner);

        if (result)
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.TrackUpdated"), trackVm.DisplayTitle);
        }
    }

    private async System.Threading.Tasks.Task SaveAsAsync(PlaylistEditorViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + PlaylistStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.Save"),
            SuggestedFileName = suggestedName,
            DefaultExtension = PlaylistStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.FileType"))
                {
                    Patterns = new[] { "*" + PlaylistStore.FileExtension }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SaveToFile(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Importar M3U / PLS

    private async void OnImportM3uClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.M3uType"))
        {
            Patterns = new[] { "*.m3u", "*.m3u8" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.ImportM3u"),
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.ImportFromExternal(path);
    }

    private async void OnImportPlsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.PlsType"))
        {
            Patterns = new[] { "*.pls" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.ImportPls"),
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.ImportFromExternal(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Exportar M3U / PLS

    private async void OnExportM3uClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        if (vm.TrackCount == 0)
        {
            vm.StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.NoTracksToExport");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + ".m3u8";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.ExportM3u"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "m3u8",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.M3u8Type"))
                {
                    Patterns = new[] { "*.m3u8" }
                },
                new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.M3uTypeShort"))
                {
                    Patterns = new[] { "*.m3u" }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.ExportToM3U(path);
    }

    private async void OnExportPlsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        if (vm.TrackCount == 0)
        {
            vm.StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.NoTracksToExport");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + PlaylistFileExtensionPls;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString("Loc.PlaylistEditor.Dialog.ExportPls"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "pls",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.PlsType"))
                {
                    Patterns = new[] { "*.pls" }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.ExportToPLS(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Generador automatico de playlists

    private async void OnGenerateClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        bool hasContent = vm.TrackCount > 0;

        var genVm = new PlaylistGeneratorWindowViewModel(
            hasExistingContent: hasContent,
            existingTrackCount: vm.TrackCount,
            existingPlaylistName: vm.PlaylistName);

        var win = new PlaylistGeneratorWindow { DataContext = genVm };

        var ok = await win.ShowDialog<bool>(owner);
        if (!ok) return;
        if (!genVm.HasPreview) return;

        // Aplicar al VM actual.
        var paths = genVm.PreviewTracks.Select(p => p.FilePath).ToList();
        vm.ReplaceTracksFromGenerator(paths);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Examinar archivos de pista

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is null) return;

        var path = await PickAudioFileAsync(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.PickMainAudio"));
        if (string.IsNullOrEmpty(path)) return;

        vm.SelectedTrack.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.SelectedTrack.Title))
            vm.SelectedTrack.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private async void OnBrowseMixClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is null) return;

        var path = await PickAudioFileAsync(LocalizationService.GetString("Loc.PlaylistEditor.Dialog.PickMixAudio"));
        if (string.IsNullOrEmpty(path)) return;

        vm.SelectedTrack.MixAudioPath = path;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers

    private const string PlaylistFileExtensionPls = ".pls";

    private async System.Threading.Tasks.Task<string?> PickAudioFileAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var audioTypes = AudioFilePickerHelper.CreateAudioFileType(
            AudioFilePickerHelper.GetSupportedExtensions(this));

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { audioTypes, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "playlist";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}
