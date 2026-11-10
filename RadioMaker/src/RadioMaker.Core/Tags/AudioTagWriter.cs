using TagLib;
// Alias para evitar la ambiguedad entre TagLib.File y System.IO.File.
using TagFile = TagLib.File;

namespace RadioMaker.Core.Tags;

/// <summary>
/// Escritor de TAGs de archivos de audio usando TagLib#.
///
/// IMPORTANTE:
///   - La escritura MODIFICA el archivo en disco. No hay undo.
///   - Algunos formatos pueden requerir que el archivo no este en uso.
///   - Se recomienda hacer backup antes de operaciones masivas (no incluido).
///
/// Uso tipico desde la UI:
///   1. AudioTagReader.Read(path) -> AudioTagInfo
///   2. new AudioTagEdit(info)   -> editor
///   3. Usuario modifica los campos
///   4. AudioTagWriter.Write(path, edit) -> guarda cambios
/// </summary>
public static class AudioTagWriter
{
    /// <summary>
    /// Resultado de una operacion de escritura.
    /// </summary>
    public sealed record WriteResult(bool Success, string? ErrorMessage)
    {
        public static readonly WriteResult Ok = new(true, null);
        public static WriteResult Fail(string message) => new(false, message);
    }

    /// <summary>
    /// Aplica los cambios al archivo. Sobrescribe los campos no vacios
    /// (excepto los que sean 0 en Year/Track/Disc, que se interpretan
    /// como "no cambiar").
    /// </summary>
    public static WriteResult Write(string filePath, AudioTagEdit edit)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return WriteResult.Fail("Ruta vacia.");
        if (edit is null)
            return WriteResult.Fail("Sin datos para escribir.");
        if (!System.IO.File.Exists(filePath))
            return WriteResult.Fail("El archivo no existe.");

        try
        {
            using var tagFile = TagFile.Create(filePath);
            var tag = tagFile.Tag;

            // Escribir todos los campos del edit (pueden quedar vacios).
            tag.Title = edit.Title ?? string.Empty;
            tag.Performers = AudioTagEdit.SplitMultiValue(edit.Performers);
            tag.Album = edit.Album ?? string.Empty;
            tag.AlbumArtists = AudioTagEdit.SplitMultiValue(edit.AlbumArtists);
            tag.Genres = AudioTagEdit.SplitMultiValue(edit.Genres);
            tag.Comment = edit.Comment ?? string.Empty;

            tag.Year = (uint)Math.Max(0, edit.Year);
            tag.Track = (uint)Math.Max(0, edit.Track);
            tag.Disc = (uint)Math.Max(0, edit.Disc);

            tagFile.Save();
            return WriteResult.Ok;
        }
        catch (Exception ex)
        {
            return WriteResult.Fail(ex.Message);
        }
    }
}