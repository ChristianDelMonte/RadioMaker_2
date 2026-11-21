namespace RadioMaker.Core.Scheduling;

/// -------------
/// Modo de reproduccion de una playlist.
/// -------------
public enum PlaylistMode
{
    /// -------------Reproduce una sola vez hasta el final y se detiene.</summary>
    Once = 0,

    /// -------------Repite continuamente desde el principio cuando termina.</summary>
    Loop = 1,

    /// -------------Reproduce en orden aleatorio.</summary>
    Shuffle = 2
}

/// -------------
/// Una playlist: lista ordenada de pistas que se reproducen en secuencia.
/// Se persiste en un archivo .rmf (JSON).
///
/// La reproduccion de una playlist se ve afectada por los programados:
/// si un programado de prioridad alta se dispara, la playlist se pausa
/// y al terminar el programado se reanuda en el punto donde quedo.
/// (Esa logica se implementara en el motor de programacion, Sprint 6.6).
/// -------------
public sealed class Playlist
{
    /// -------------Formato del archivo, para version futura.</summary>
    public int Version { get; set; } = 1;

    /// -------------Identificador unico de la playlist.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// -------------Nombre visible.</summary>
    public string Name { get; set; } = "Nueva Playlist";

    /// -------------Pistas en orden de reproduccion.</summary>
    public List<PlaylistTrack> Tracks { get; set; } = new();

    /// -------------Modo de reproduccion (una vez, loop, aleatorio).</summary>
    public PlaylistMode Mode { get; set; } = PlaylistMode.Once;

    /// -------------
    /// ID del bus de salida asignado a esta playlist. Se persiste en el
    /// .rmf para que la configuracion viaje con el archivo.
    ///
    /// Reglas:
    ///   - Vacio = usar el bus del modulo Scheduler (el "aire").
    ///   - Si al abrir en otra PC el bus no existe, se cae al del Scheduler.
    ///   - Solo el usuario puede cambiarlo desde el combo de la toolbar.
    /// -------------
    public string OutputBusId { get; set; } = string.Empty;

    /// -------------Comentarios libres.</summary>
    public string Notes { get; set; } = string.Empty;

    /// -------------Fecha de creacion (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// -------------Fecha de ultima modificacion (UTC).</summary>
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// -------------Cantidad de pistas activas.</summary>
    public int EnabledTrackCount => Tracks.Count(t => t.IsEnabled);

    /// -------------Nombre a mostrar (Name o valor por defecto).</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name : "Nueva Playlist";
}