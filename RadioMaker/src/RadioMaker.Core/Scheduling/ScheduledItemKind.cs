namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Tipo de item programado. Determina que tipo de contenido tiene.
/// </summary>
public enum ScheduledItemKind
{
    /// <summary>Un solo archivo de audio (usa ScheduledItem.FilePath).</summary>
    SingleFile = 0,

    /// <summary>Una tanda con multiples archivos (usa ScheduledItem.EmbeddedTanda).</summary>
    Tanda = 1,

    /// <summary>
    /// Una playlist completa (usa ScheduledItem.FilePath apuntando a un .rmf).
    /// La playlist se arma y edita en su propia pestaña; aca solo se referencia.
    /// </summary>
    Playlist = 2,

    /// <summary>
    /// Un Bloque de Programacion: secuencia ordenada de archivos, playlists
    /// y tandas que se reproducen de corrido cuando el bloque se dispara.
    /// Usa ScheduledItem.EmbeddedBlock.
    /// </summary>
    Block = 3
}