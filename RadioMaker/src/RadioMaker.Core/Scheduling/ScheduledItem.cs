using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

public enum SchedulePriority
{
    Low = 0,
    High = 1
}

public sealed class ScheduledItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ScheduledItemKind Kind { get; set; } = ScheduledItemKind.SingleFile;

    /// <summary>
    /// Ruta del contenido. Para SingleFile es el archivo de audio (.mp3, .flac, etc).
    /// Para Playlist es el archivo .rmf. Para Tanda y Block no se usa.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public SchedulePriority Priority { get; set; } = SchedulePriority.Low;
    public List<ScheduleEntry> Entries { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public double DurationSeconds { get; set; } = 0.0;

    public Tanda? EmbeddedTanda { get; set; }

    /// <summary>
    /// Bloque embebido. Solo aplica cuando Kind == Block. Contiene la
    /// secuencia ordenada de items (archivos, playlists, tandas) que
    /// se reproducen de corrido al dispararse el bloque.
    /// </summary>
    public ScheduledBlock? EmbeddedBlock { get; set; }

    /// <summary>
    /// Marca si este item ya fue disparado en la sesion actual.
    /// Flag de runtime — no se persiste en el archivo .rms.
    /// Se resetea al hacer "Reiniciar disparos" en el panel del scheduler.
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title;

            if (Kind == ScheduledItemKind.Tanda && EmbeddedTanda is not null)
                return EmbeddedTanda.DisplayName;

            if (Kind == ScheduledItemKind.Block && EmbeddedBlock is not null)
                return EmbeddedBlock.DisplayName;

            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                // Para playlist mostramos el nombre sin la extension del .rmf
                if (Kind == ScheduledItemKind.Playlist)
                    return Path.GetFileNameWithoutExtension(FilePath);

                return Path.GetFileName(FilePath);
            }

            return "(sin contenido)";
        }
    }
}