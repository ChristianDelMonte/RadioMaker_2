namespace RadioMaker.Core.Tags;

/// -------------
/// Modelo editable de TAGs de un archivo de audio.
/// Se usa junto con <see cref="AudioTagWriter"/> para modificar los
/// metadatos de un archivo.
///
/// Se separa de <see cref="AudioTagInfo"/> porque este ultimo incluye
/// campos derivados (duracion, bitrate, sample rate, canales) que no
/// se escriben, sino que se leen del archivo.
/// -------------
public sealed class AudioTagEdit
{
    public string Title { get; set; } = string.Empty;

    /// -------------Multiples artistas separados por "; " en la UI. Al escribir, se dividen.</summary>
    public string Performers { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    /// -------------Multiples album artists separados por "; ".</summary>
    public string AlbumArtists { get; set; } = string.Empty;

    /// -------------Multiples generos separados por "; ".</summary>
    public string Genres { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Track { get; set; }
    public int Disc { get; set; }

    public string Comment { get; set; } = string.Empty;

    // ───── Constructor ───────────────────────────────────────────────────

    public AudioTagEdit() { }

    /// -------------Crea un AudioTagEdit a partir de un AudioTagInfo leido.</summary>
    public AudioTagEdit(AudioTagInfo info)
    {
        Title = info.Title;
        Performers = info.Performers;
        Album = info.Album;
        AlbumArtists = info.AlbumArtists;
        Genres = info.Genres;
        Year = info.Year;
        Track = info.Track;
        Disc = info.Disc;
        Comment = info.Comment;
    }

    /// -------------Divide una cadena "A; B; C" en un array, ignorando vacios.</summary>
    internal static string[] SplitMultiValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();

        return value
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}