using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Preset del generador automatico de playlists. Se guarda como archivo
/// .rpg (JSON autocontenido).
///
/// Incluye:
///   - Metadata: nombre, descripcion, fechas.
///   - Options: la configuracion completa del generador (carpeta, modo,
///     orden, filtros y reglas de variedad).
///
/// La carpeta de origen se guarda tambien, pero es solo un "default" —
/// el usuario puede cambiarla al aplicar el preset (por ejemplo si abre
/// el .rpg en otra maquina o quiere reutilizar la config con otra carpeta).
/// -------------
public sealed class PlaylistGeneratorPreset
{
    public int Version { get; set; } = 1;

    /// -------------Nombre visible del preset.</summary>
    public string Name { get; set; } = "Nuevo Preset";

    /// -------------Descripcion libre (opcional).</summary>
    public string Description { get; set; } = string.Empty;

    /// -------------Configuracion completa del generador.</summary>
    public PlaylistGeneratorOptions Options { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// -------------Nombre a mostrar (cae a "Sin nombre" si esta vacio).</summary>
    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name : "Sin nombre";
}