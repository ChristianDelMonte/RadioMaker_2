using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;  // (si no lo tenés ya)
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Services;
namespace RadioMaker.UI.Views;

/// <summary>
/// Code-behind del editor de playlists. Maneja los dialogos nativos de
/// archivos .rmf, M3U/M3U8 y PLS, mas la seleccion de archivos de audio
/// y mixados de cada pista.
/// </summary>
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

        var filter = new FilePickerFileType("Playlist .rmf")
        {
            Patterns = new[] { "*" + PlaylistStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir playlist (.rmf)",
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
                vm.StatusMessage = error ?? "No se pudo abrir en RMWave.";
        }
        else
        {
            if (vm is not null)
                vm.StatusMessage = $"Abriendo {System.IO.Path.GetFileName(track.FilePath)} en RMWave...";
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
        var win = new PlaylistTrackEditorWindow { DataContext = editorVm };

        var result = await win.ShowDialog<bool>(owner);

        if (result)
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = $"Pista actualizada: {trackVm.DisplayTitle}";
        }
    }

    private async System.Threading.Tasks.Task SaveAsAsync(PlaylistEditorViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + PlaylistStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar playlist (.rmf)",
            SuggestedFileName = suggestedName,
            DefaultExtension = PlaylistStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Playlist .rmf")
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

        var filter = new FilePickerFileType("Playlist M3U / M3U8")
        {
            Patterns = new[] { "*.m3u", "*.m3u8" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar playlist desde M3U / M3U8",
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

        var filter = new FilePickerFileType("Playlist PLS")
        {
            Patterns = new[] { "*.pls" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar playlist desde PLS",
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
            vm.StatusMessage = "No hay pistas para exportar.";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + ".m3u8";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar playlist a M3U / M3U8",
            SuggestedFileName = suggestedName,
            DefaultExtension = "m3u8",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Playlist M3U8")
                {
                    Patterns = new[] { "*.m3u8" }
                },
                new FilePickerFileType("Playlist M3U")
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
            vm.StatusMessage = "No hay pistas para exportar.";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.PlaylistName) + PlaylistFileExtensionPls;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar playlist a PLS",
            SuggestedFileName = suggestedName,
            DefaultExtension = "pls",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Playlist PLS")
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

        var path = await PickAudioFileAsync("Seleccionar archivo de audio principal");
        if (string.IsNullOrEmpty(path)) return;

        vm.SelectedTrack.FilePath = path;

        if (string.IsNullOrWhiteSpace(vm.SelectedTrack.Title))
            vm.SelectedTrack.Title = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private async void OnBrowseMixClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedTrack is null) return;

        var path = await PickAudioFileAsync("Seleccionar archivo de mixado");
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

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "playlist";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}