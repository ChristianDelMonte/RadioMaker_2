using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel de la pestaña de una carpeta favorita.
///
/// A diferencia del explorador completo, este panel:
///   - No tiene arbol de directorios.
///   - No tiene filtro de busqueda.
///   - No tiene boton de refresh manual.
///   - Solo muestra la lista de archivos de UNA carpeta especifica.
///
/// Pero SI tiene:
///   - Drag&drop de archivos hacia otros modulos.
///   - Editar tags con click derecho.
///   - Panel de info/tags abajo del archivo seleccionado.
///
/// La carpeta se carga una sola vez al abrir la pestaña. Si el usuario
/// quiere ver contenido actualizado, cierra y vuelve a abrir la pestaña.
/// -------------
public sealed partial class FavoriteFolderPanelViewModel : ViewModelBase, IDisposable
{
    private readonly FavoriteFolderEntry _entry;
    private readonly IAudioEngine _engine;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    // ───── Identidad del favorito ────────────────────────────────────────
    public string FavoriteId => _entry.Id;
    public string FolderPath => _entry.Path;
    public string DisplayName => _entry.EffectiveName;
    public bool FolderExists => _entry.ExistsNow;

    // ───── Estado ────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.FavoriteFolder.Status.Starting");
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private AudioFileViewModel? _selectedFile;
    [ObservableProperty] private int _fileCount;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _nowPlayingText = string.Empty;

    /// -------------
    /// Reservado para cuando FavoriteFolder participe del BusExclusivityCoordinator.
    /// Hoy siempre es false: las preescuchas no se pausan por coordinacion.
    /// -------------
    public bool IsPausedByCoordinator => false;

    public ObservableCollection<AudioFileViewModel> Files { get; } = new();

    /// -------------
    /// Selector de bus de salida propio de esta pestaña. Al reproducir un
    /// archivo, el canal se carga en este bus y no afecta otras pestañas.
    /// -------------
    public SimpleBusSelectorViewModel BusSelector { get; }

    public bool HasFiles => Files.Count > 0;
    public bool HasNoFiles => !HasFiles;
    public bool HasSelection => SelectedFile is not null;
    public bool HasNoSelection => !HasSelection;

    public string FileCountText => FileCount == 0
        ? LocalizationService.GetString("Loc.FavoriteFolder.Status.NoAudioFiles")
        : $"{FileCount} archivo(s)";

    /// -------------Se dispara cuando el usuario activa un archivo (doble clic / Enter).</summary>
    public event EventHandler<AudioFileViewModel>? FileActivated;

    // ───── Constructor ───────────────────────────────────────────────────
    private IAudioChannel? _currentChannel;
    internal IAudioChannel? PreviewChannel => _currentChannel;
    public FavoriteFolderPanelViewModel(FavoriteFolderEntry entry, IAudioEngine engine)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        // Cargar el bus persistido para este favorito (si existe) y guardar
        // cuando el usuario lo cambie.
        var savedBusId = RightPanelBusStore.GetFavoriteBusId(entry.Id);
        IAudioBus? initialBus = null;

        if (!string.IsNullOrEmpty(savedBusId))
        {
            try
            {
                initialBus = engine.Buses.FirstOrDefault(b => b.Id == savedBusId);
            }
            catch { }
        }

        BusSelector = new SimpleBusSelectorViewModel(
            engine,
            initialBus: initialBus,
            onBusChanged: bus => RightPanelBusStore.SetFavoriteBusId(entry.Id, bus.Id));

        _ = LoadFilesAsync();
    }

    // ───── Carga de archivos ─────────────────────────────────────────────

    public async Task LoadFilesAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        Files.Clear();
        FileCount = 0;
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(FileCountText));

        if (!_entry.ExistsNow)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.Missing"), _entry.Path);
            return;
        }

        IsLoading = true;
        StatusMessage = LocalizationService.GetString("Loc.FavoriteFolder.Status.Reading");

        try
        {
            var supported = GetSupportedExtensions();
            var files = await Task.Run(() => EnumerateAudioFiles(_entry.Path, supported), cts.Token);
            if (cts.IsCancellationRequested) return;

            FileCount = files.Count;
            foreach (var f in files)
            {
                if (cts.IsCancellationRequested) return;
                Files.Add(f);
            }

            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(FileCountText));

            StatusMessage = files.Count == 0
                ? LocalizationService.GetString("Loc.FavoriteFolder.Status.Empty")
                : string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.LoadingTags"), files.Count);

            _ = LoadTagsAsync(Files.ToList(), cts.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.ReadError"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private HashSet<string> GetSupportedExtensions()
    {
        try
        {
            return new HashSet<string>(
                _engine.SupportedExtensions.Select(e => e.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(
                new[] { ".mp3", ".ogg", ".wav", ".aif", ".aiff", ".flac", ".m4a", ".aac", ".opus", ".wv" },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static List<AudioFileViewModel> EnumerateAudioFiles(string folder, HashSet<string> supported)
    {
        var result = new List<AudioFileViewModel>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                if (!supported.Contains(ext)) continue;

                try { result.Add(new AudioFileViewModel(path)); } catch { }
            }
        }
        catch { }

        return result.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task LoadTagsAsync(List<AudioFileViewModel> files, CancellationToken token)
    {
        int loaded = 0;

        foreach (var file in files)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                await file.LoadTagsAsync();
                loaded++;

                if (loaded % 5 == 0)
                    StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.TagProgress"), loaded, files.Count);
            }
            catch { }
        }

        if (!token.IsCancellationRequested)
            StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.Ready"), files.Count);
    }

    // ───── Activacion y seleccion ────────────────────────────────────────

    partial void OnSelectedFileChanged(AudioFileViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));

        if (value is not null && !value.TagsLoaded)
            _ = value.LoadTagsAsync();
    }

    public void ActivateSelectedFile()
    {
        if (SelectedFile is null) return;

        // Disparar el evento (por si algo lo consume en el futuro).
        FileActivated?.Invoke(this, SelectedFile);

        // Y reproducir directamente en el bus propio de esta pestaña.
        PlayFile(SelectedFile.FullPath);
    }

    // ───── Reproduccion ──────────────────────────────────────────────────

    /// -------------
    /// Reproduce un archivo en el bus seleccionado. Si habia otro sonando
    /// en esta pestaña, se detiene antes. No afecta la reproduccion de
    /// otras pestañas ni de otros modulos.
    /// -------------
    public void PlayFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (!File.Exists(filePath)) return;

        if (BusSelector.SelectedBus is not IAudioBus bus)
        {
            StatusMessage = LocalizationService.GetString("Loc.FavoriteFolder.Status.NoOutputBus");
            return;
        }

        try
        {
            StopCurrentChannel();

            var channel = bus.LoadFile(filePath);
            channel.Ended += OnChannelEnded;
            _currentChannel = channel;

            channel.Play();

            IsPlaying = true;
            NowPlayingText = Path.GetFileName(filePath);
            StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.Playing"), NowPlayingText);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.FavoriteFolder.Status.PlayError"), ex.Message);
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        StopCurrentChannel();
        IsPlaying = false;
        NowPlayingText = string.Empty;
        StatusMessage = LocalizationService.GetString("Loc.FavoriteFolder.Status.Stopped");
    }

    private void StopCurrentChannel()
    {
        var ch = _currentChannel;
        _currentChannel = null;
        if (ch is null) return;

        try
        {
            ch.Ended -= OnChannelEnded;
            ch.Stop();
            ch.Dispose();
        }
        catch { }
    }

    private void OnChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            NowPlayingText = string.Empty;
            StopCurrentChannel();
        });
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _loadCts?.Cancel(); } catch { }
        StopCurrentChannel();
    }
}
