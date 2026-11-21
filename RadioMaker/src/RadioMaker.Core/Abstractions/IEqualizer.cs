namespace RadioMaker.Core.Abstractions;

/// -------------
/// Ecualizador gráfico de N bandas aplicable a un canal de audio.
/// Cada banda tiene una frecuencia central fija y una ganancia ajustable en dB.
/// -------------
public interface IEqualizer : IDisposable
{
    /// -------------Cantidad de bandas del ecualizador.</summary>
    int BandCount { get; }

    /// -------------Frecuencias centrales (Hz) de cada banda. Índice 0 = graves, último = agudos.</summary>
    IReadOnlyList<float> CenterFrequencies { get; }

    /// -------------Estado de habilitación. Si está deshabilitado, no procesa audio.</summary>
    bool IsEnabled { get; }

    /// -------------Ganancia mínima y máxima permitida por banda (en dB).</summary>
    double MinGainDb { get; }
    double MaxGainDb { get; }

    /// -------------Habilita o deshabilita el procesamiento.</summary>
    void SetEnabled(bool enabled);

    /// -------------Establece la ganancia de una banda (índice 0..BandCount-1).</summary>
    void SetBandGain(int bandIndex, double gainDb);

    /// -------------Obtiene la ganancia actual de una banda.</summary>
    double GetBandGain(int bandIndex);

    /// -------------Aplica todas las ganancias de una sola vez.</summary>
    void ApplyGains(IReadOnlyList<double> gainsDb);

    /// -------------Resetea todas las bandas a 0 dB.</summary>
    void ResetAll();
}
