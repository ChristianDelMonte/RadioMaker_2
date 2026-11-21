namespace RadioMaker.Core.Abstractions;

/// -------------
/// Info sobre un addon de formato cargado en BASS.
/// Cada addon agrega soporte para decodificar uno o mas formatos de audio
/// (por ejemplo, bassflac habilita la decodificacion de archivos .flac).
/// -------------
public sealed record FormatPluginInfo(
    /// -------------Nombre legible del addon (ej: "FLAC", "AAC", "Opus").</summary>
    string Name,

    /// -------------Extensiones de archivo soportadas, con punto (ej: ".flac").</summary>
    IReadOnlyList<string> Extensions,

    /// -------------Version del addon (tal como la reporta BASS).</summary>
    Version Version,

    /// -------------true si el addon se cargo exitosamente.</summary>
    bool IsLoaded)
{
    public override string ToString() => IsLoaded
        ? $"{Name} v{Version} ({string.Join(", ", Extensions)})"
        : $"{Name} (no disponible)";
}
