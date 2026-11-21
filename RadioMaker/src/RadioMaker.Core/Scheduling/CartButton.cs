using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Un boton de la botonera (Modulo 3B).
/// Representa un audio corto que se dispara al presionar un boton
/// (jingle, cortina, efecto, ID, etc).
/// -------------
public sealed class CartButton
{
    /// -------------Identificador unico, estable a traves de la persistencia.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// -------------Ruta absoluta del archivo de audio. Vacio = boton sin asignar.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// -------------Etiqueta visible sobre el boton. Si esta vacio, se usa el nombre del archivo.</summary>
    public string Label { get; set; } = string.Empty;

    /// -------------Color de fondo del boton en formato #RRGGBB.</summary>
    public string Color { get; set; } = "#2A3050";

    /// -------------
    /// Tecla de atajo opcional (ej. "1", "F1", "Numpad1").
    /// Vacio = sin atajo asignado.
    /// -------------
    public string Hotkey { get; set; } = string.Empty;

    /// -------------Si esta habilitado. Un boton deshabilitado no reproduce.</summary>
    public bool IsEnabled { get; set; } = true;

    /// -------------Ajuste de volumen relativo (dB) sobre el volumen del bus.</summary>
    public double VolumeDb { get; set; } = 0.0;

    /// -------------Duracion del archivo en segundos. Se completa al asignar el archivo.</summary>
    public double DurationSeconds { get; set; } = 0.0;

    /// -------------Si el boton debe reproducirse en loop hasta que se vuelva a presionar.</summary>
    public bool Loop { get; set; } = false;

    /// -------------Notas libres para el operador.</summary>
    public string Notes { get; set; } = string.Empty;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    /// -------------Etiqueta a mostrar: Label si esta cargado, sino nombre del archivo.</summary>
    [JsonIgnore]
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label)
            ? Label
            : (!string.IsNullOrWhiteSpace(FilePath)
                ? Path.GetFileNameWithoutExtension(FilePath)
                : string.Empty);

    /// -------------True si el boton tiene un archivo asignado.</summary>
    [JsonIgnore]
    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    /// -------------Duracion formateada.</summary>
    [JsonIgnore]
    public string DurationText => AudioFileInfoReader.FormatDuration(DurationSeconds);
}