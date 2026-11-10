namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Modelo serializable que describe la configuración de un Bus de audio.
/// Se persiste en disco en formato JSON (ver <c>BusConfigurationStore</c>).
/// </summary>
public sealed class AudioBusConfig
{
    /// <summary>Identificador único del bus (no cambia durante su vida).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nombre visible, editable por el usuario.</summary>
    public string DisplayName { get; set; } = "Nuevo Bus";

    /// <summary>
    /// Índice del dispositivo de salida según BASS:
    /// <c>-1</c> = predeterminado del sistema; <c>0..N-1</c> = dispositivos específicos.
    /// </summary>
    public int DeviceIndex { get; set; } = -1;

    /// <summary>Volumen master del bus (0.0 .. 1.0).</summary>
    public double MasterVolume { get; set; } = 1.0;

    /// <summary>
    /// Indica si es el bus principal. El bus principal se usa cuando la UI
    /// no especifica un bus concreto (ej. reproducción rápida).
    /// </summary>
    public bool IsPrimary { get; set; }
}
