namespace RadioMaker.Core.Abstractions;

/// -------------
/// Modelo serializable que describe la configuración de un Bus de audio.
/// Se persiste en disco en formato JSON (ver <c>BusConfigurationStore</c>).
/// -------------
public sealed class AudioBusConfig
{
    /// -------------Identificador único del bus (no cambia durante su vida).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// -------------Nombre visible, editable por el usuario.</summary>
    public string DisplayName { get; set; } = "Nuevo Bus";

    /// -------------
    /// Índice del dispositivo de salida según BASS:
    /// <c>-1</c> = predeterminado del sistema; <c>0..N-1</c> = dispositivos específicos.
    /// -------------
    public int DeviceIndex { get; set; } = -1;

    /// -------------Volumen master del bus (0.0 .. 1.0).</summary>
    public double MasterVolume { get; set; } = 1.0;

    /// -------------
    /// Indica si es el bus principal. El bus principal se usa cuando la UI
    /// no especifica un bus concreto (ej. reproducción rápida).
    /// -------------
    public bool IsPrimary { get; set; }
}
