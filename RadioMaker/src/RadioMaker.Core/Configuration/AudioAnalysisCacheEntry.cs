using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Configuration;

/// <summary>
/// Entrada del cache persistente de analisis de audio (.rmt).
/// Guarda el resultado del analisis de silencios y niveles para un archivo
/// de audio concreto, junto con metadata para invalidar el cache si el
/// archivo fue modificado.
///
/// El cache es unico por archivo (clave = hash del path absoluto).
/// </summary>
public sealed class AudioAnalysisCacheEntry
{
    /// <summary>Version del formato. Cambiar cuando el schema cambie.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Path absoluto original del audio (para diagnostico y validacion).</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Tamanio del archivo en bytes en el momento del analisis.</summary>
    public long FileSize { get; set; }

    /// <summary>Fecha de ultima modificacion (UTC) en el momento del analisis.</summary>
    public DateTime LastWriteTimeUtc { get; set; }

    /// <summary>Resultado del analisis de silencios.</summary>
    public SilenceAnalysisResult Silence { get; set; } = SilenceAnalysisResult.NotAnalyzed;

    /// <summary>Resultado del analisis de niveles (peak + RMS).</summary>
    public NormalizationResult Levels { get; set; } = NormalizationResult.NotAnalyzed;
}