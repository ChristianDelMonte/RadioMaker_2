namespace RadioMaker.Core.Scheduling;

/// -------------
/// Tipo de item programado. Determina que tipo de contenido tiene.
/// -------------
public enum ScheduledItemKind
{
    /// -------------Un solo archivo de audio (usa ScheduledItem.FilePath).</summary>
    SingleFile = 0,

    /// -------------Una tanda con multiples archivos (usa ScheduledItem.EmbeddedTanda).</summary>
    Tanda = 1,

    /// -------------
    /// Una playlist completa (usa ScheduledItem.FilePath apuntando a un .rmf).
    /// La playlist se arma y edita en su propia pestaña; aca solo se referencia.
    /// -------------
    Playlist = 2,

    /// -------------
    /// Un Bloque de Programacion: secuencia ordenada de archivos, playlists
    /// y tandas que se reproducen de corrido cuando el bloque se dispara.
    /// Usa ScheduledItem.EmbeddedBlock.
    /// -------------
    Block = 3
}