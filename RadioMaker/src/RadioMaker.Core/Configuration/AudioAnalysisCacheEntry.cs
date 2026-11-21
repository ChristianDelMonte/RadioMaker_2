using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Configuration;

/// -------------
/// Entrada del cache persistente de analisis de audio (.rmt).
/// Guarda el resultado del analisis de silencios y niveles para un archivo
/// de audio concreto, junto con metadata para invalidar el cache si el
/// archivo fue modificado.
///
/// El cache es unico por archivo (clave = hash del path absoluto).
/// -------------
public sealed class AudioAnalysisCacheEntry
{
    /// -------------Version del formato. Cambiar cuando el schema cambie.</summary>
    public int Version { get; set; } = 1;

    /// -------------Path absoluto original del audio (para diagnostico y validacion).</summary>
    public string FullPath { get; set; } = "";

    /// -------------Tamanio del archivo en bytes en el momento del analisis.</summary>
    public long FileSize { get; set; }

    /// -------------Fecha de ultima modificacion (UTC) en el momento del analisis.</summary>
    public DateTime LastWriteTimeUtc { get; set; }

    /// -------------Resultado del analisis de silencios.</summary>
    public SilenceAnalysisResult Silence { get; set; } = SilenceAnalysisResult.NotAnalyzed;

    /// -------------Resultado del analisis de niveles (peak + RMS).</summary>
    public NormalizationResult Levels { get; set; } = NormalizationResult.NotAnalyzed;
}