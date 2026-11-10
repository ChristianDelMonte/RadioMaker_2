using TagLib;
// Alias para evitar la ambiguedad entre TagLib.File y System.IO.File.
using TagFile = TagLib.File;

namespace RadioMaker.Core.Tags;

/// <summary>
/// Lector de TAGs de archivos de audio usando TagLib#.
///
/// TagLib# soporta: MP3 (ID3v1/ID3v2), FLAC, Ogg Vorbis, MP4/M4A, WMA,
/// WAV, AIFF, APE, MPC, Speex y mas.
///
/// IMPORTANTE: la lectura abre el archivo para leer sus metadata. Es una
/// operacion de I/O ligera (~5-20 ms por archivo) pero que igual conviene
/// hacer en un hilo secundario desde la UI.
/// </summary>
public static class AudioTagReader
{
    /// <summary>
    /// Lee los TAGs del archivo indicado. Devuelve Empty si falla o el
    /// archivo no tiene metadata.
    /// </summary>
    public static AudioTagInfo Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return AudioTagInfo.Empty;
        if (!System.IO.File.Exists(filePath)) return AudioTagInfo.Empty;

        try
        {
            using var tagFile = TagFile.Create(filePath);

            var tag = tagFile.Tag;
            var props = tagFile.Properties;

            bool hasCover = tag.Pictures is { Length: > 0 };
            byte[]? coverData = ExtractCoverData(tag);

            return new AudioTagInfo(
                Title: tag.Title ?? string.Empty,
                Performers: string.Join(", ", tag.Performers ?? Array.Empty<string>()),
                Album: tag.Album ?? string.Empty,
                AlbumArtists: string.Join(", ", tag.AlbumArtists ?? Array.Empty<string>()),
                Genres: string.Join(", ", tag.Genres ?? Array.Empty<string>()),
                Year: (int)tag.Year,
                Track: (int)tag.Track,
                Disc: (int)tag.Disc,
                Duration: props?.Duration ?? TimeSpan.Zero,
                Bitrate: props?.AudioBitrate ?? 0,
                SampleRate: props?.AudioSampleRate ?? 0,
                Channels: props?.AudioChannels ?? 0,
                Comment: tag.Comment ?? string.Empty,
                HasCoverArt: hasCover,
                CoverData: coverData);
        }
        catch
        {
            return AudioTagInfo.Empty;
        }
    }

    /// <summary>
    /// Extrae los bytes de la primera imagen embebida del archivo.
    /// Devuelve null si no hay imagen o si falla la lectura.
    ///
    /// Se hace una copia defensiva del array subyacente del ByteVector
    /// porque TagLib puede reutilizar el buffer al liberar el archivo.
    /// </summary>
    private static byte[]? ExtractCoverData(TagLib.Tag tag)
    {
        try
        {
            if (tag.Pictures is not { Length: > 0 })
                return null;

            var pic = tag.Pictures[0];
            if (pic?.Data is null)
                return null;

            var raw = pic.Data.Data;
            if (raw is null || raw.Length == 0)
                return null;

            var copy = new byte[raw.Length];
            Array.Copy(raw, copy, raw.Length);
            return copy;
        }
        catch
        {
            return null;
        }
    }
}