using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Un Bloque de Programacion: secuencia ordenada de items que se reproducen
/// de corrido (playlists, tandas y archivos sueltos) cuando el scheduler
/// dispara el bloque.
///
/// Diferencias con Tanda:
///   - Tanda contiene pistas (archivos de audio) con ajustes por pista.
///   - Bloque contiene REFERENCIAS a contenido (archivos, playlists, tandas
///     completas). Al dispararse, el motor lo "aplana" a una tanda virtual
///     y lo reproduce de corrido.
///
/// El bloque NO respeta los horarios de sus items internos. Solo el bloque
/// tiene horario. Los items internos se reproducen en secuencia pura.
///
/// NO se permite anidar bloques dentro de bloques.
/// -------------
public sealed class ScheduledBlock
{
    /// -------------Identificador unico del bloque.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// -------------Nombre visible del bloque.</summary>
    public string Name { get; set; } = "Nuevo Bloque";

    /// -------------Items que componen el bloque, en orden de reproduccion.</summary>
    public List<ScheduledBlockItem> Items { get; set; } = new();

    /// -------------Comentarios libres.</summary>
    public string Notes { get; set; } = string.Empty;

    /// -------------Titulo para mostrar.</summary>
    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name : "Nuevo Bloque";

    /// -------------Cantidad de items en el bloque.</summary>
    [JsonIgnore]
    public int ItemCount => Items.Count;
}

/// -------------
/// Un item dentro de un Bloque. Es una REFERENCIA a contenido en disco:
///   - SingleFile: ruta a un archivo de audio.
///   - Playlist:   ruta a un archivo .rmf.
///   - Tanda:      ruta a un archivo .tnd.
///
/// Los items del bloque NO tienen horarios propios. Solo el bloque los tiene.
/// -------------
public sealed class ScheduledBlockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public ScheduledItemKind Kind { get; set; } = ScheduledItemKind.SingleFile;

    /// -------------
    /// Ruta al contenido:
    ///   - SingleFile: archivo de audio (.mp3, .flac, etc)
    ///   - Playlist:   archivo .rmf
    ///   - Tanda:      archivo .tnd
    /// -------------
    public string FilePath { get; set; } = string.Empty;

    /// -------------Titulo opcional. Si esta vacio, se usa el nombre del archivo.</summary>
    public string Title { get; set; } = string.Empty;

    /// -------------
    /// Duracion total del contenido en segundos. Se calcula al vuelo cuando
    /// se asigna un archivo y se persiste para no recalcular cada vez que
    /// se reabre el editor. 0 = no calculada todavia.
    /// -------------
    public double DurationSeconds { get; set; } = 0.0;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title;
            if (string.IsNullOrWhiteSpace(FilePath)) return "(sin contenido)";

            return Kind switch
            {
                ScheduledItemKind.Playlist => Path.GetFileNameWithoutExtension(FilePath),
                ScheduledItemKind.Tanda => Path.GetFileNameWithoutExtension(FilePath),
                _ => Path.GetFileName(FilePath)
            };
        }
    }

    [JsonIgnore]
    public string KindText => Kind switch
    {
        ScheduledItemKind.Playlist => "PLAYLIST",
        ScheduledItemKind.Tanda => "TANDA",
        _ => "ARCHIVO"
    };

    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(FilePath);
}