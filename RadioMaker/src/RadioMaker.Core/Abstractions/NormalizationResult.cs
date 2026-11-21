namespace RadioMaker.Core.Abstractions;

/// -------------
/// Resultado del analisis de niveles de un archivo de audio.
///
/// Contiene el PEAK maximo y el RMS promedio del archivo completo,
/// medidos en dBFS (decibeles respecto a full scale, con 0 dBFS = maximo
/// digital posible).
///
/// Se calcula decodificando el archivo offline, sin reproducirlo.
/// La ganancia sugerida depende del modo de normalizacion deseado:
///   - Modo Peak: GainDb = TargetPeakDb - PeakDb
///   - Modo Rms:  GainDb = TargetRmsDb  - RmsDb   (limitado por el peak)
/// -------------
public sealed record NormalizationResult(
    /// -------------true si el analisis se ejecuto. false si el archivo no se analizo todavia.</summary>
    bool IsAnalyzed,

    /// -------------Duracion total del archivo.</summary>
    TimeSpan TotalDuration,

    /// -------------Peak maximo del archivo en dBFS. Tipicamente entre -60 y 0.</summary>
    double PeakDb,

    /// -------------RMS promedio del archivo en dBFS. Tipicamente entre -40 y -10.</summary>
    double RmsDb,

    /// -------------Cantidad total de samples procesados (informativo).</summary>
    long SampleCount)
{
    /// -------------Resultado "vacio" - usado antes de analizar.</summary>
    public static readonly NormalizationResult NotAnalyzed = new(
        IsAnalyzed: false,
        TotalDuration: TimeSpan.Zero,
        PeakDb: -120.0,
        RmsDb: -120.0,
        SampleCount: 0);

    /// -------------
    /// Factor de cresta: diferencia entre peak y RMS.
    /// Un valor alto (> 15 dB) indica audio muy dinamico (clasico, jazz).
    /// Un valor bajo (&lt; 8 dB) indica audio muy comprimido (pop, EDM).
    /// -------------
    public double CrestFactorDb => PeakDb - RmsDb;

    /// -------------
    /// Calcula la ganancia (en dB) necesaria para alcanzar un target
    /// segun el modo elegido. Aplica limitaciones para evitar clipping.
    /// -------------
    /// <param name="mode">Estrategia de normalizacion.</param>
    /// <param name="targetDb">Target en dBFS (ej: -1 para peak, -16 para RMS).</param>
    /// <param name="maxPeakDb">Techo maximo de peak permitido (ej: -0.5 dBFS).</param>
    public double ComputeGainDb(
        NormalizationMode mode,
        double targetDb,
        double maxPeakDb = -0.5)
    {
        if (!IsAnalyzed) return 0.0;

        double gain;

        if (mode == NormalizationMode.Peak)
        {
            // Peak normalization: subir/bajar para que el peak llegue al target.
            gain = targetDb - PeakDb;
        }
        else
        {
            // RMS normalization: subir/bajar para que el RMS llegue al target,
            // pero sin que el peak resultante supere maxPeakDb.
            gain = targetDb - RmsDb;

            // Limitar si al aplicar la ganancia el peak superaria el techo.
            double resultingPeak = PeakDb + gain;
            if (resultingPeak > maxPeakDb)
                gain = maxPeakDb - PeakDb;
        }

        // Redondear a 2 decimales y limitar el rango util.
        return Math.Round(Math.Clamp(gain, -60.0, +60.0), 2);
    }
}