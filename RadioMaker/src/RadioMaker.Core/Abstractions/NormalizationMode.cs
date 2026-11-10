namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Estrategia de normalizacion de niveles.
/// </summary>
public enum NormalizationMode
{
    /// <summary>
    /// Lleva el PEAK maximo del archivo al target (por defecto -1 dBFS).
    /// Garantiza que no haya clipping. Conserva la dinamica natural del audio.
    /// </summary>
    Peak = 0,

    /// <summary>
    /// Lleva el RMS promedio al target (por defecto -16 dBFS).
    /// Empareja la sonoridad percibida entre archivos. Puede requerir
    /// reduccion si el peak resultante supera 0 dBFS.
    /// </summary>
    Rms = 1
}