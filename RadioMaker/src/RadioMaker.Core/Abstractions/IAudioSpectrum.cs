namespace RadioMaker.Core.Abstractions;

/// -------------
/// Fuente de datos de analisis espectral (FFT) y de forma de onda para un
/// canal de audio.
///
/// Se usa para alimentar analizadores de espectro graficos en la UI.
/// -------------
public interface IAudioSpectrum
{
    /// -------------Cantidad de canales (mono = 1, estereo = 2).</summary>
    int ChannelCount { get; }

    /// -------------
    /// Cantidad de bins de FFT que devuelve <see cref="ReadFft"/>. Para
    /// FFT2048 son 1024 bins (el primero es DC y se suele ignorar).
    /// -------------
    int FftBinCount { get; }

    /// -------------Frecuencia de muestreo en Hz (util para rotular el eje X).</summary>
    int SampleRate { get; }

    /// -------------
    /// Lee los datos de FFT actuales del canal y los copia en
    /// <paramref name="buffer"/>. El buffer debe tener al menos
    /// <see cref="FftBinCount"/> elementos.
    ///
    /// Devuelve false si no hay datos disponibles.
    /// -------------
    bool ReadFft(float[] buffer);

    /// -------------
    /// Lee muestras crudas del canal (para dibujar la forma de onda).
    /// <paramref name="buffer"/> debe tener capacidad para
    /// <paramref name="frameCount"/> * <see cref="ChannelCount"/> floats.
    ///
    /// Devuelve false si no hay datos disponibles.
    /// -------------
    bool ReadWaveform(float[] buffer, int frameCount);
}