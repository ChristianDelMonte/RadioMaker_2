using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Scheduling;

public static class AudioAnalysis
{
    public static SilenceAnalysisResult AnalyzeSilence(
        string filePath,
        double thresholdDb = -50.0,
        int blockMs = 50,
        int minSilenceMs = 300)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return SilenceAnalysisResult.NotAnalyzed;

        // ===== INTEGRACIÓN RMWave =====
        // Los .rmw requerirían render offline para analizar. Por ahora se saltea.
        if (filePath.EndsWith(".rmw", System.StringComparison.OrdinalIgnoreCase))
            return SilenceAnalysisResult.NotAnalyzed;

        return Engine.BassSilenceAnalyzer.Analyze(filePath, thresholdDb, blockMs, minSilenceMs);
    }

    public static NormalizationResult AnalyzeLevels(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return NormalizationResult.NotAnalyzed;

        if (filePath.EndsWith(".rmw", System.StringComparison.OrdinalIgnoreCase))
            return NormalizationResult.NotAnalyzed;

        return Engine.BassLevelAnalyzer.Analyze(filePath);
    }
}