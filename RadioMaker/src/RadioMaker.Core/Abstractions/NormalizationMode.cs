namespace RadioMaker.Core.Abstractions;

/// -------------
/// Estrategia de normalizacion de niveles.
/// -------------
public enum NormalizationMode
{
    /// -------------
    /// Lleva el PEAK maximo del archivo al target (por defecto -1 dBFS).
    /// Garantiza que no haya clipping. Conserva la dinamica natural del audio.
    /// -------------
    Peak = 0,

    /// -------------
    /// Lleva el RMS promedio al target (por defecto -16 dBFS).
    /// Empareja la sonoridad percibida entre archivos. Puede requerir
    /// reduccion si el peak resultante supera 0 dBFS.
    /// -------------
    Rms = 1
}