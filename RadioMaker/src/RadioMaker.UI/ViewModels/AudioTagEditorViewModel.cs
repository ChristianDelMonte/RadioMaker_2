using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Tags;
using Avalonia.Media.Imaging;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del editor de TAGs de un archivo de audio.
///
/// Trabaja sobre una COPIA de los valores del archivo. Los cambios solo se
/// escriben al disco cuando el usuario presiona "Guardar". Si cancela, el
/// archivo queda intacto.
///
/// Al guardar exitosamente, recarga los TAGs del <see cref="AudioFileViewModel"/>
/// original para que la lista y el panel de info inferior se actualicen solos.
/// -------------
public sealed partial class AudioTagEditorViewModel : ViewModelBase
{
    private readonly AudioFileViewModel _file;

    // ───── Campos editables ──────────────────────────────────────────────

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _performers;
    [ObservableProperty] private string _album;
    [ObservableProperty] private string _albumArtists;
    [ObservableProperty] private string _genres;
    [ObservableProperty] private int _year;
    [ObservableProperty] private int _track;
    [ObservableProperty] private int _disc;
    [ObservableProperty] private string _comment;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ───── Info de solo lectura (para el header) ─────────────────────────

    public string FileName => _file.FileName;
    public string FullPath => _file.FullPath;
    public string DurationText => _file.Tags?.DurationText ?? "--:--";
    public string BitrateText => _file.Tags?.BitrateText ?? string.Empty;
    public string SampleRateText => _file.Tags?.SampleRateText ?? string.Empty;
    public string ChannelsText => _file.Tags?.ChannelsText ?? string.Empty;
    public bool HasCoverArt => _file.Tags?.HasCoverArt == true;
    /// -------------
    /// Caratula embebida del archivo (Bitmap), o null si no tiene.
    /// Se delega al <see cref="AudioFileViewModel"/> original, que ya la
    /// cachea internamente y la invalida cuando los tags se recargan.
    /// -------------
    public Bitmap? CoverArt => _file.CoverArt;

    public string TechnicalLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(DurationText) && DurationText != "--:--") parts.Add(DurationText);
            if (!string.IsNullOrWhiteSpace(BitrateText)) parts.Add(BitrateText);
            if (!string.IsNullOrWhiteSpace(SampleRateText)) parts.Add(SampleRateText);
            if (!string.IsNullOrWhiteSpace(ChannelsText)) parts.Add(ChannelsText);
            return string.Join("  ·  ", parts);
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public AudioTagEditorViewModel(AudioFileViewModel file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));

        var t = file.Tags ?? AudioTagInfo.Empty;

        _title = t.Title;
        _performers = t.Performers;
        _album = t.Album;
        _albumArtists = t.AlbumArtists;
        _genres = t.Genres;
        _year = t.Year;
        _track = t.Track;
        _disc = t.Disc;
        _comment = t.Comment;
    }

    // ───── Guardar ───────────────────────────────────────────────────────

    /// -------------
    /// Escribe los valores actuales al archivo. Devuelve true si la escritura
    /// fue exitosa. En caso de exito, recarga los tags del VM original.
    /// -------------
    public async Task<bool> SaveAsync()
    {
        try
        {
            var edit = new AudioTagEdit
            {
                Title = Title ?? string.Empty,
                Performers = Performers ?? string.Empty,
                Album = Album ?? string.Empty,
                AlbumArtists = AlbumArtists ?? string.Empty,
                Genres = Genres ?? string.Empty,
                Year = Math.Max(0, Year),
                Track = Math.Max(0, Track),
                Disc = Math.Max(0, Disc),
                Comment = Comment ?? string.Empty,
            };

            var result = AudioTagWriter.Write(_file.FullPath, edit);

            if (!result.Success)
            {
                HasError = true;
                StatusMessage = string.Format(LocalizationService.GetString("Loc.AudioTags.Status.SaveError"), result.ErrorMessage);
                return false;
            }

            // Refrescar la lista y el panel de info inferior.
            await _file.ReloadTagsAsync();

            HasError = false;
            StatusMessage = LocalizationService.GetString("Loc.AudioTags.Status.Saved");
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AudioTags.Status.UnexpectedError"), ex.Message);
            return false;
        }
    }

    /// -------------Limpia el mensaje de estado (se usa al editar cualquier campo).</summary>
    public void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasError = false;
    }
}
