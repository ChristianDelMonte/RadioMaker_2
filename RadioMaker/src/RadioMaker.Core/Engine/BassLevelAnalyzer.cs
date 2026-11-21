using System.Runtime.InteropServices;
using ManagedBass;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// -------------
/// Analiza los niveles (peak maximo y RMS promedio) de un archivo de audio
/// decodificandolo en memoria, SIN reproducirlo.
///
/// Estrategia:
///   1. Abrir un stream TEMPORAL en modo decode-only (BassFlags.Decode)
///      con muestras en FLOAT (BassFlags.Float). Valores en rango -1.0 .. +1.0.
///   2. Leer el archivo en bloques grandes (1 segundo) para eficiencia.
///   3. Para cada bloque:
///        - Actualizar el peak: max(abs(sample)) entre todos los samples.
///        - Acumular la suma de cuadrados para el RMS.
///   4. Al final calcular:
///        - PeakDb = 20 * log10(peak)
///        - RmsDb  = 20 * log10(sqrt(sumSq / totalSamples))
///   5. Liberar el stream temporal.
///
/// IMPORTANTE: este metodo es SINCRONICO y puede tardar 1-3 segundos en
/// archivos largos. SIEMPRE se debe llamar desde un hilo secundario (Task.Run).
/// -------------
internal static class BassLevelAnalyzer
{
    /// -------------Bloque de lectura por iteracion: 1 segundo (freq * channels floats).</summary>
    private const int TargetBlockSeconds = 1;

    public static NormalizationResult Analyze(string filePath)
    {
        int streamHandle = 0;

        try
        {
            streamHandle = Bass.CreateStream(
                filePath,
                0L,
                0L,
                BassFlags.Decode | BassFlags.Float);

            if (streamHandle == 0)
                return NormalizationResult.NotAnalyzed;

            if (!Bass.ChannelGetInfo(streamHandle, out var info))
                return NormalizationResult.NotAnalyzed;

            int frequency = info.Frequency;
            int channels = info.Channels;
            if (frequency <= 0 || channels <= 0)
                return NormalizationResult.NotAnalyzed;

            long lengthBytes = Bass.ChannelGetLength(streamHandle, PositionFlags.Bytes);
            if (lengthBytes <= 0)
                return NormalizationResult.NotAnalyzed;

            double totalSeconds = Bass.ChannelBytes2Seconds(streamHandle, lengthBytes);
            var totalDuration = TimeSpan.FromSeconds(totalSeconds);

            // Bloque de 1 segundo (o el total si el archivo es mas corto).
            int blockFrames = frequency * TargetBlockSeconds;
            int blockSamples = blockFrames * channels;
            int blockBytes = blockSamples * sizeof(float);

            var buffer = new float[blockSamples];
            var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            double peak = 0.0;
            double sumSq = 0.0;
            long totalSamples = 0;

            try
            {
                IntPtr bufferPtr = gcHandle.AddrOfPinnedObject();

                while (true)
                {
                    int readBytes = Bass.ChannelGetData(streamHandle, bufferPtr, blockBytes);
                    if (readBytes <= 0) break;

                    int samplesRead = readBytes / sizeof(float);
                    if (samplesRead <= 0) break;

                    // Actualizar peak y sumSq en una sola pasada.
                    for (int i = 0; i < samplesRead; i++)
                    {
                        double s = buffer[i];
                        double abs = s < 0 ? -s : s;
                        if (abs > peak) peak = abs;
                        sumSq += s * s;
                    }

                    totalSamples += samplesRead;
                }
            }
            finally
            {
                gcHandle.Free();
            }

            if (totalSamples == 0)
                return NormalizationResult.NotAnalyzed;

            double rms = Math.Sqrt(sumSq / totalSamples);

            // Convertir a dBFS. Valores <= 1e-9 => -120 dB (practicamente silencio).
            double peakDb = peak <= 1e-9 ? -120.0 : 20.0 * Math.Log10(peak);
            double rmsDb  = rms  <= 1e-9 ? -120.0 : 20.0 * Math.Log10(rms);

            // Peak no puede superar 0 dBFS en teoria, pero clipeos podrian dar
            // valores levemente por encima. Lo limitamos al rango fisico.
            if (peakDb > 0.0) peakDb = 0.0;

            return new NormalizationResult(
                IsAnalyzed: true,
                TotalDuration: totalDuration,
                PeakDb: Math.Round(peakDb, 2),
                RmsDb: Math.Round(rmsDb, 2),
                SampleCount: totalSamples);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error analizando niveles: {ex.Message}");
            return NormalizationResult.NotAnalyzed;
        }
        finally
        {
            if (streamHandle != 0)
            {
                try { Bass.StreamFree(streamHandle); }
                catch { }
            }
        }
    }
}