namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Una tanda: grupo de archivos de audio que se reproducen en secuencia como
/// una unidad. Se puede programar y agregar a cualquier lista .rms como un
/// item mas, o guardarse por separado en un archivo .tnd.
/// </summary>
public sealed class Tanda
{
    /// <summary>Formato del archivo, para detectar versiones futuras.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Identificador unico de la tanda.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nombre de la tanda (se usa como titulo por defecto).</summary>
    public string Name { get; set; } = "Nueva Tanda";

    /// <summary>Pistas que componen la tanda, en orden de reproduccion.</summary>
    public List<TandaTrack> Tracks { get; set; } = new();

    /// <summary>Comentarios libres.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Fecha de creacion (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Fecha de ultima modificacion (UTC).</summary>
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Cantidad de pistas activas.</summary>
    public int EnabledTrackCount => Tracks.Count(t => t.IsEnabled);

    /// <summary>Titulo para mostrar (Name o "Nueva Tanda" si esta vacio).</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name : "Nueva Tanda";
}