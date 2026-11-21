namespace RadioMaker.Core.Abstractions;

/// -------------
/// Configuracion del procesador de ducking automatico.
///
/// El ducking baja el volumen del programa cuando el microfono detecta voz.
/// Cuando el locutor deja de hablar durante un tiempo, el volumen vuelve
/// a subir suavemente.
/// -------------
public sealed class DuckingConfig
{
    /// -------------Si true, el procesador esta activo.</summary>
    public bool IsEnabled { get; set; }

    /// -------------Indice del dispositivo de entrada segun BASS (-1 = predeterminado).</summary>
    public int MicDeviceIndex { get; set; } = -1;

    /// -------------
    /// Nivel del microfono (dBFS) por encima del cual se considera que hay voz.
    /// Tipicamente entre -50 y -30. Mas bajo = mas sensible.
    /// -------------
    public double ThresholdDb { get; set; } = -40.0;

    /// -------------
    /// Reduccion de ganancia aplicada al programa mientras hay voz (en dB, valor positivo).
    /// Ejemplo: 15.0 significa que el programa baja 15 dB.
    /// -------------
    public double ReductionDb { get; set; } = 15.0;

    /// -------------
    /// Tiempo (ms) que se mantiene la reduccion despues del ultimo sonido detectado.
    /// Evita que la musica vuelva a subir en pausas naturales del habla.
    /// -------------
    public int HoldMs { get; set; } = 400;

    /// -------------
    /// Tiempo (ms) que tarda la ganancia en bajar cuando empieza la voz.
    /// Valores chicos = respuesta rapida pero mas agresiva.
    /// -------------
    public int AttackMs { get; set; } = 80;

    /// -------------
    /// Tiempo (ms) que tarda la ganancia en volver a subir cuando termina la voz.
    /// Valores grandes = transicion mas suave.
    /// -------------
    public int ReleaseMs { get; set; } = 600;

    /// -------------Clonar para pasar una copia segura al procesador.</summary>
    public DuckingConfig Clone() => new()
    {
        IsEnabled = IsEnabled,
        MicDeviceIndex = MicDeviceIndex,
        ThresholdDb = ThresholdDb,
        ReductionDb = ReductionDb,
        HoldMs = HoldMs,
        AttackMs = AttackMs,
        ReleaseMs = ReleaseMs,
    };
}