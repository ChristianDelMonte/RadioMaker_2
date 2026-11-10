namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Ecualizador gráfico de N bandas aplicable a un canal de audio.
/// Cada banda tiene una frecuencia central fija y una ganancia ajustable en dB.
/// </summary>
public interface IEqualizer : IDisposable
{
    /// <summary>Cantidad de bandas del ecualizador.</summary>
    int BandCount { get; }

    /// <summary>Frecuencias centrales (Hz) de cada banda. Índice 0 = graves, último = agudos.</summary>
    IReadOnlyList<float> CenterFrequencies { get; }

    /// <summary>Estado de habilitación. Si está deshabilitado, no procesa audio.</summary>
    bool IsEnabled { get; }

    /// <summary>Ganancia mínima y máxima permitida por banda (en dB).</summary>
    double MinGainDb { get; }
    double MaxGainDb { get; }

    /// <summary>Habilita o deshabilita el procesamiento.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Establece la ganancia de una banda (índice 0..BandCount-1).</summary>
    void SetBandGain(int bandIndex, double gainDb);

    /// <summary>Obtiene la ganancia actual de una banda.</summary>
    double GetBandGain(int bandIndex);

    /// <summary>Aplica todas las ganancias de una sola vez.</summary>
    void ApplyGains(IReadOnlyList<double> gainsDb);

    /// <summary>Resetea todas las bandas a 0 dB.</summary>
    void ResetAll();
}
