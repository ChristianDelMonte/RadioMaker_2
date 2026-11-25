using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;
using RadioMaker.Core.Tags;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Tipo de archivo que puede aparecer en el explorador.
/// -------------
public enum AudioFileKind
{
    Audio = 0,
    Playlist = 1,
    Tanda = 2,
    ScheduledList = 3,
    CartBoard = 4
}

/// -------------
/// Representa un archivo en la lista del File Manager.
///
/// Puede ser un archivo de audio (mp3, flac, etc) o un proyecto nativo
/// de RadioMaker (.rmf / .tnd / .rms / .rcb). El tipo se detecta por
/// extension en el constructor.
///
/// Carga lazy de TAGs: al construirse solo tiene el path y el tamaño.
/// Los metadatos completos (titulo, artista, duracion, caratula, etc) se
/// leen on-demand mediante LoadTagsAsync cuando el usuario lo selecciona
/// o cuando el listado esta listo. Para archivos de proyecto, los TAGs
/// no aplican.
/// -------------
public sealed partial class AudioFileViewModel : ViewModelBase
{
    /// -------------Ruta absoluta del archivo.</summary>
    public string FullPath { get; }

    /// -------------Nombre del archivo con extension.</summary>
    public string FileName { get; }

    /// -------------Extension en minusculas (.mp3, .flac, .rmf, etc).</summary>
    public string Extension { get; }

    /// -------------Tamaño en bytes.</summary>
    public long SizeBytes { get; }

    /// -------------Fecha de ultima modificacion.</summary>
    public DateTime LastModified { get; }

    /// -------------Tipo detectado por la extension.</summary>
    public AudioFileKind Kind { get; }

    /// -------------True si es un archivo de audio reproducible.</summary>
    public bool IsAudio => Kind == AudioFileKind.Audio;

    /// -------------
    /// True si es un proyecto nativo de RadioMaker (.rmf / .tnd / .rms / .rcb).
    /// Los proyectos no se reproducen directamente desde el explorador; se
    /// arrastran a la pestaña que corresponda.
    /// -------------
    public bool IsNativeProject => !IsAudio;

    /// -------------Etiqueta del tipo (vacia para audios).</summary>
    public string KindText => Kind switch
    {
        AudioFileKind.Playlist => LocalizationService.GetString("Loc.ScheduledItem.Kind.Playlist"),
        AudioFileKind.Tanda => LocalizationService.GetString("Loc.ScheduledItem.Kind.Sequence"),
        AudioFileKind.ScheduledList => LocalizationService.GetString("Loc.Label.Module.Scheduler"),
        AudioFileKind.CartBoard => LocalizationService.GetString("Loc.Label.FileKind.CartBoard"),
        _ => string.Empty
    };

    /// -------------Color de la etiqueta del tipo (clave de recurso).</summary>
    public string KindColor => Kind switch
    {
        AudioFileKind.Playlist => "Success",
        AudioFileKind.Tanda => "Accent",
        AudioFileKind.ScheduledList => "Warning",
        AudioFileKind.CartBoard => "Accent",
        _ => "FgMuted"
    };

    // ───── Estado de TAGs (lazy) ─────────────────────────────────────────

    private bool _tagsLoaded;

    [ObservableProperty] private AudioTagInfo? _tags;
    [ObservableProperty] private bool _isLoadingTags;

    public bool TagsLoaded => _tagsLoaded;

    // ───── Caratula (cacheada) ───────────────────────────────────────────

    private Bitmap? _cachedCover;
    private bool _coverResolved;

    public Bitmap? CoverArt
    {
        get
        {
            if (_coverResolved) return _cachedCover;
            _coverResolved = true;
            _cachedCover = LoadCover(Tags?.CoverData);
            return _cachedCover;
        }
    }

    private static Bitmap? LoadCover(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(data);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string DisplayTitle
    {
        get
        {
            if (IsNativeProject)
                return Path.GetFileNameWithoutExtension(FileName);

            if (Tags is not null && !string.IsNullOrWhiteSpace(Tags.Title))
                return Tags.Title;

            return Path.GetFileNameWithoutExtension(FileName);
        }
    }

    public string ArtistAlbum => IsNativeProject ? string.Empty : (Tags?.ArtistAlbum ?? string.Empty);

    public string DurationText => IsNativeProject
        ? "—"
        : (Tags?.DurationText ?? "--:--");

    public string SizeText
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F0} KB";
            if (SizeBytes < 1024L * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
            return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");

    public string DetailText
    {
        get
        {
            if (IsNativeProject)
                return KindText;

            if (Tags is null) return string.Empty;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Tags.ArtistAlbum))
                parts.Add(Tags.ArtistAlbum);

            if (!string.IsNullOrWhiteSpace(Tags.BitrateText))
                parts.Add(Tags.BitrateText);

            if (!string.IsNullOrWhiteSpace(Tags.SampleRateText))
                parts.Add(Tags.SampleRateText);

            return string.Join(" · ", parts);
        }
    }

    public bool HasCoverArt => !IsNativeProject && Tags?.HasCoverArt == true;

    // ───── Lineas del panel de tags ──────────────────────────────────────

    public string TagsLineArtistAlbum => IsNativeProject ? string.Empty : (Tags?.ArtistAlbum ?? string.Empty);

    public string TagsLineMeta
    {
        get
        {
            if (IsNativeProject) return string.Empty;
            if (Tags is null) return string.Empty;

            var parts = new List<string>();
            if (Tags.Year > 0) parts.Add(Tags.Year.ToString());
            if (!string.IsNullOrWhiteSpace(Tags.Genres)) parts.Add(Tags.Genres);
            if (Tags.Track > 0) parts.Add($"Pista {Tags.Track}");
            if (Tags.Disc > 1) parts.Add($"Disco {Tags.Disc}");
            return string.Join("  ·  ", parts);
        }
    }

    public string TagsLineTechnical
    {
        get
        {
            if (IsNativeProject)
                return $"{KindText}  ·  {SizeText}  ·  {LastModifiedText}";

            if (Tags is null) return string.Empty;

            var parts = new List<string>();
            if (Tags.Duration.TotalSeconds > 0) parts.Add(Tags.DurationText);
            if (!string.IsNullOrWhiteSpace(Tags.BitrateText)) parts.Add(Tags.BitrateText);
            if (!string.IsNullOrWhiteSpace(Tags.SampleRateText)) parts.Add(Tags.SampleRateText);
            if (!string.IsNullOrWhiteSpace(Tags.ChannelsText)) parts.Add(Tags.ChannelsText);
            return string.Join("  ·  ", parts);
        }
    }

    public string TagsComment => IsNativeProject ? string.Empty : (Tags?.Comment ?? string.Empty);

    public bool HasComment => !string.IsNullOrWhiteSpace(TagsComment);

    // ───── Constructor ───────────────────────────────────────────────────

    public AudioFileViewModel(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        Extension = Path.GetExtension(fullPath).ToLowerInvariant();

        Kind = DetectKind(Extension);

        try
        {
            var info = new FileInfo(fullPath);
            SizeBytes = info.Length;
            LastModified = info.LastWriteTime;
        }
        catch
        {
            SizeBytes = 0;
            LastModified = DateTime.MinValue;
        }
    }

    private static AudioFileKind DetectKind(string extLower)
    {
        if (string.Equals(extLower, PlaylistStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return AudioFileKind.Playlist;

        if (string.Equals(extLower, TandaStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return AudioFileKind.Tanda;

        if (string.Equals(extLower, ScheduledListStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return AudioFileKind.ScheduledList;

        if (string.Equals(extLower, CartBoardStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            return AudioFileKind.CartBoard;

        return AudioFileKind.Audio;
    }

    // ───── Carga de TAGs (lazy) ──────────────────────────────────────────

    /// -------------
    /// Lee los TAGs del archivo en background. Idempotente.
    /// Para archivos de proyecto, no hace I/O: los TAGs no aplican.
    /// -------------
    public async Task LoadTagsAsync()
    {
        if (_tagsLoaded) return;

        if (IsNativeProject)
        {
            Tags = AudioTagInfo.Empty;
            _tagsLoaded = true;
            NotifyTagsChanged();
            return;
        }

        IsLoadingTags = true;
        try
        {
            var info = await Task.Run(() => AudioTagReader.Read(FullPath));
            Tags = info;
            _tagsLoaded = true;
            NotifyTagsChanged();
        }
        catch
        {
            Tags = AudioTagInfo.Empty;
            _tagsLoaded = true;
            NotifyTagsChanged();
        }
        finally
        {
            IsLoadingTags = false;
        }
    }

    public async Task ReloadTagsAsync()
    {
        _tagsLoaded = false;
        _coverResolved = false;
        _cachedCover = null;
        await LoadTagsAsync();
    }

    private void NotifyTagsChanged()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ArtistAlbum));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(HasCoverArt));
        OnPropertyChanged(nameof(TagsLoaded));
        OnPropertyChanged(nameof(CoverArt));
        OnPropertyChanged(nameof(TagsLineArtistAlbum));
        OnPropertyChanged(nameof(TagsLineMeta));
        OnPropertyChanged(nameof(TagsLineTechnical));
        OnPropertyChanged(nameof(TagsComment));
        OnPropertyChanged(nameof(HasComment));
    }

    partial void OnTagsChanged(AudioTagInfo? value)
    {
        _coverResolved = false;
        _cachedCover = null;
        NotifyTagsChanged();
    }
}
