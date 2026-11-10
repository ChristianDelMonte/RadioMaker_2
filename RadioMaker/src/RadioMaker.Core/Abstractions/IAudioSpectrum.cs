namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Fuente de datos de analisis espectral (FFT) y de forma de onda para un
/// canal de audio.
///
/// Se usa para alimentar analizadores de espectro graficos en la UI.
/// </summary>
public interface IAudioSpectrum
{
    /// <summary>Cantidad de canales (mono = 1, estereo = 2).</summary>
    int ChannelCount { get; }

    /// <summary>
    /// Cantidad de bins de FFT que devuelve <see cref="ReadFft"/>. Para
    /// FFT2048 son 1024 bins (el primero es DC y se suele ignorar).
    /// </summary>
    int FftBinCount { get; }

    /// <summary>Frecuencia de muestreo en Hz (util para rotular el eje X).</summary>
    int SampleRate { get; }

    /// <summary>
    /// Lee los datos de FFT actuales del canal y los copia en
    /// <paramref name="buffer"/>. El buffer debe tener al menos
    /// <see cref="FftBinCount"/> elementos.
    ///
    /// Devuelve false si no hay datos disponibles.
    /// </summary>
    bool ReadFft(float[] buffer);

    /// <summary>
    /// Lee muestras crudas del canal (para dibujar la forma de onda).
    /// <paramref name="buffer"/> debe tener capacidad para
    /// <paramref name="frameCount"/> * <see cref="ChannelCount"/> floats.
    ///
    /// Devuelve false si no hay datos disponibles.
    /// </summary>
    bool ReadWaveform(float[] buffer, int frameCount);
}