namespace RadioMaker.Core.Abstractions;

/// -------------
/// Resultado del analisis de silencios de un archivo de audio.
///
/// Silencio = bloque de audio cuyo nivel RMS esta por debajo de un umbral
/// (por defecto -50 dB). Se analiza el archivo completo en modo "decode only",
/// sin reproducirlo.
/// -------------
public sealed record SilenceAnalysisResult(
    /// -------------true si el analisis se ejecuto. false si el archivo no se analizo todavia.</summary>
    bool IsAnalyzed,

    /// -------------Duracion total del archivo.</summary>
    TimeSpan TotalDuration,

    /// -------------Duracion del silencio al inicio del archivo.</summary>
    TimeSpan StartSilence,

    /// -------------Duracion del silencio al final del archivo.</summary>
    TimeSpan EndSilence,

    /// -------------Cantidad de silencios internos detectados (>= 300 ms cada uno).</summary>
    int InternalSilenceCount,

    /// -------------Duracion total sumada de todos los silencios internos.</summary>
    TimeSpan InternalSilenceTotal,

    /// -------------Umbral en dB por debajo del cual se considera silencio.</summary>
    double ThresholdDb)
{
    /// -------------Resultado "vacio" — usado antes de analizar.</summary>
    public static readonly SilenceAnalysisResult NotAnalyzed = new(
        IsAnalyzed: false,
        TotalDuration: TimeSpan.Zero,
        StartSilence: TimeSpan.Zero,
        EndSilence: TimeSpan.Zero,
        InternalSilenceCount: 0,
        InternalSilenceTotal: TimeSpan.Zero,
        ThresholdDb: -50.0);

    /// -------------Duracion sin contar los silencios inicial y final.</summary>
    public TimeSpan EffectiveDuration =>
        TotalDuration > StartSilence + EndSilence
            ? TotalDuration - StartSilence - EndSilence
            : TimeSpan.Zero;

    /// -------------true si hay mas de 100 ms de silencio al inicio o al final.</summary>
    public bool HasSignificantTrim =>
        StartSilence.TotalMilliseconds > 100 || EndSilence.TotalMilliseconds > 100;
}