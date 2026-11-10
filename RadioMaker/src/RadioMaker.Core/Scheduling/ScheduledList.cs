namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Lista completa de programados. Se persiste en un archivo .rms (JSON).
///
/// Cada pestaña "Programados" del Modulo 3A muestra el contenido de una
/// ScheduledList. El archivo .rms es la unidad de guardado/carga.
/// </summary>
public sealed class ScheduledList
{
    /// <summary>Formato del archivo, para detectar versiones futuras.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Identificador unico e inmutable de la lista. Se persiste en el .rms
    /// y se usa para agrupar items en el panel de Eventos, para el registry,
    /// y para tracking del scheduler.
    /// Si el .rms es viejo y no tiene este campo, se asigna uno nuevo al
    /// cargar (via default del constructor + System.Text.Json).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nombre de la lista (el titulo de la pestaña lo usa como default).</summary>
    public string Name { get; set; } = "Programados";

    /// <summary>Items programados.</summary>
    public List<ScheduledItem> Items { get; set; } = new();

    /// <summary>
    /// Si es <c>true</c>, el motor de programacion procesa esta lista y sus
    /// items se disparan segun sus horarios. Si es <c>false</c>, el motor
    /// la ignora por completo (util para listas en armado).
    /// Se persiste en el .rms para sobrevivir al reinicio.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Fecha de creacion (UTC ISO 8601).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Fecha de ultima modificacion (UTC ISO 8601).</summary>
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Nombre para mostrar (Name o "Programados" si esta vacio).</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name : "Programados";
}