namespace RadioMaker.Core.Tags;

/// -------------
/// Informacion de TAGs de un archivo de audio (leida con TagLib#).
///
/// <c>CoverData</c> contiene los bytes crudos de la caratula embebida
/// (JPEG/PNG). Se guardan como byte[] para mantener el modelo independiente
/// del framework de UI; la conversion a Bitmap la hace el ViewModel.
/// -------------
public sealed record AudioTagInfo(
    string Title,
    string Performers,       // "Artista1, Artista2"
    string Album,
    string AlbumArtists,
    string Genres,           // "Rock, Pop"
    int Year,
    int Track,
    int Disc,
    TimeSpan Duration,
    int Bitrate,             // kbps
    int SampleRate,          // Hz
    int Channels,
    string Comment,
    bool HasCoverArt,
    byte[]? CoverData)
{
    /// -------------Resultado vacio para archivos sin TAGs.</summary>
    public static readonly AudioTagInfo Empty = new(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        0, 0, 0, TimeSpan.Zero, 0, 0, 0, string.Empty, false, null);

    /// -------------Titulo para mostrar: Title si esta, sino vacio.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title : string.Empty;

    /// -------------Descripcion "Artista - Album" o solo uno de ellos.</summary>
    public string ArtistAlbum
    {
        get
        {
            var artist = !string.IsNullOrWhiteSpace(Performers) ? Performers : AlbumArtists;

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(Album))
                return $"{artist} — {Album}";
            if (!string.IsNullOrWhiteSpace(artist))
                return artist;
            if (!string.IsNullOrWhiteSpace(Album))
                return Album;
            return string.Empty;
        }
    }

    /// -------------Duracion formateada como m:ss (o h:mm:ss si es largo).</summary>
    public string DurationText
    {
        get
        {
            if (Duration.TotalSeconds < 1) return "--:--";
            if (Duration.TotalHours >= 1) return Duration.ToString(@"h\:mm\:ss");
            return Duration.ToString(@"m\:ss");
        }
    }

    public string BitrateText => Bitrate > 0 ? $"{Bitrate} kbps" : string.Empty;
    public string SampleRateText => SampleRate > 0 ? $"{SampleRate} Hz" : string.Empty;
    public string ChannelsText => Channels switch
    {
        1 => "Mono",
        2 => "Estereo",
        > 0 => $"{Channels} canales",
        _ => string.Empty
    };
}