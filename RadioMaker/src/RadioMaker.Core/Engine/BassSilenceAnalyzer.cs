using System.Runtime.InteropServices;
using ManagedBass;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// -------------
/// Analiza silencios de un archivo de audio decodificandolo en memoria,
/// SIN reproducirlo.
///
/// Estrategia:
///   1. Abrir un stream TEMPORAL en modo decode-only (BassFlags.Decode)
///      con muestras en FLOAT (BassFlags.Float) para facilitar el calculo de RMS.
///   2. Leer el archivo en bloques de N milisegundos.
///   3. Calcular el RMS de cada bloque y clasificarlo como silencio si
///      cae por debajo del umbral configurado.
///   4. Post-procesar la lista de bloques para determinar:
///        - Silencio inicial (bloques silenciosos al principio)
///        - Silencio final   (bloques silenciosos al final)
///        - Silencios internos (bloques silenciosos consecutivos >= minSilenceMs)
///   5. Liberar el stream temporal.
///
/// IMPORTANTE: este metodo es SINCRONICO y puede tardar varios segundos en
/// archivos largos. SIEMPRE se debe llamar desde un hilo secundario (Task.Run).
/// -------------
internal static class BassSilenceAnalyzer
{
    public static SilenceAnalysisResult Analyze(
        string filePath,
        double thresholdDb = -50.0,
        int blockMs = 50,
        int minSilenceMs = 300)
    {
        int streamHandle = 0;

        try
        {
            // 1) Abrir el archivo en modo DECODE + FLOAT. Esto no usa el
            //    dispositivo de salida y no interfiere con la reproduccion.
            streamHandle = Bass.CreateStream(
                filePath,
                0L,
                0L,
                BassFlags.Decode | BassFlags.Float);

            if (streamHandle == 0)
                return SilenceAnalysisResult.NotAnalyzed;

            // 2) Info del stream: sample rate, canales, duracion.
            if (!Bass.ChannelGetInfo(streamHandle, out var info))
                return SilenceAnalysisResult.NotAnalyzed;

            long lengthBytes = Bass.ChannelGetLength(streamHandle, PositionFlags.Bytes);
            if (lengthBytes <= 0)
                return SilenceAnalysisResult.NotAnalyzed;

            double totalSeconds = Bass.ChannelBytes2Seconds(streamHandle, lengthBytes);
            var totalDuration = TimeSpan.FromSeconds(totalSeconds);

            // 3) Calcular tamano de bloque en BYTES y en muestras.
            //    Un float = 4 bytes. Un frame = channels floats.
            int frequency = info.Frequency;
            int channels = info.Channels;
            if (frequency <= 0 || channels <= 0)
                return SilenceAnalysisResult.NotAnalyzed;

            int blockFrames = Math.Max(1, frequency * blockMs / 1000);
            int blockSamples = blockFrames * channels;
            int blockBytes = blockSamples * sizeof(float);

            // 4) Buffer managed reutilizable + pin para pasar a BASS.
            var buffer = new float[blockSamples];
            var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            var silenceFlags = new List<bool>();

            try
            {
                IntPtr bufferPtr = gcHandle.AddrOfPinnedObject();

                while (true)
                {
                    int readBytes = Bass.ChannelGetData(streamHandle, bufferPtr, blockBytes);
                    if (readBytes <= 0) break;

                    int samplesRead = readBytes / sizeof(float);
                    if (samplesRead <= 0) break;

                    // Calcular RMS del bloque.
                    double sumSq = 0.0;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        double s = buffer[i];
                        sumSq += s * s;
                    }
                    double rms = Math.Sqrt(sumSq / samplesRead);

                    // Convertir a dB. rms = 0 -> -Infinito (consideramos silencio).
                    double db = rms <= 1e-9 ? -120.0 : 20.0 * Math.Log10(rms);
                    bool isSilent = db < thresholdDb;
                    silenceFlags.Add(isSilent);
                }
            }
            finally
            {
                gcHandle.Free();
            }

            // 5) Post-procesar la lista de bloques.
            return BuildResult(silenceFlags, blockMs, minSilenceMs, totalDuration, thresholdDb);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error analizando silencios: {ex.Message}");
            return SilenceAnalysisResult.NotAnalyzed;
        }
        finally
        {
            if (streamHandle != 0)
            {
                try { Bass.StreamFree(streamHandle); }
                catch { /* best effort */ }
            }
        }
    }

    // -----------------------------------------------------------------------

    private static SilenceAnalysisResult BuildResult(
        List<bool> silenceFlags,
        int blockMs,
        int minSilenceMs,
        TimeSpan totalDuration,
        double thresholdDb)
    {
        if (silenceFlags.Count == 0)
            return new SilenceAnalysisResult(
                IsAnalyzed: true,
                TotalDuration: totalDuration,
                StartSilence: TimeSpan.Zero,
                EndSilence: TimeSpan.Zero,
                InternalSilenceCount: 0,
                InternalSilenceTotal: TimeSpan.Zero,
                ThresholdDb: thresholdDb);

        int total = silenceFlags.Count;

        // --- Silencio inicial ---
        int startSilentBlocks = 0;
        while (startSilentBlocks < total && silenceFlags[startSilentBlocks])
            startSilentBlocks++;

        var startSilence = TimeSpan.FromMilliseconds((double)startSilentBlocks * blockMs);

        // --- Silencio final ---
        int endSilentBlocks = 0;
        int idx = total - 1;
        while (idx >= 0 && silenceFlags[idx])
        {
            endSilentBlocks++;
            idx--;
        }

        // Evitar solapamiento entre inicio y fin cuando el archivo es
        // completamente silencioso.
        if (startSilentBlocks + endSilentBlocks > total)
            endSilentBlocks = Math.Max(0, total - startSilentBlocks);

        var endSilence = TimeSpan.FromMilliseconds((double)endSilentBlocks * blockMs);

        // --- Silencios internos ---
        int minSilentBlocksForInternal = Math.Max(1, minSilenceMs / blockMs);

        int internalCount = 0;
        int internalBlocks = 0;

        // Recorremos SOLO la zona interna (excluyendo los tramos de inicio/fin).
        int internalStart = startSilentBlocks;
        int internalEnd = total - endSilentBlocks;

        int runLength = 0;
        for (int i = internalStart; i < internalEnd; i++)
        {
            if (silenceFlags[i])
            {
                runLength++;
            }
            else
            {
                if (runLength >= minSilentBlocksForInternal)
                {
                    internalCount++;
                    internalBlocks += runLength;
                }
                runLength = 0;
            }
        }

        // Cerrar el ultimo run si quedo colgando al llegar al final interno.
        if (runLength >= minSilentBlocksForInternal)
        {
            internalCount++;
            internalBlocks += runLength;
        }

        var internalTotal = TimeSpan.FromMilliseconds((double)internalBlocks * blockMs);

        return new SilenceAnalysisResult(
            IsAnalyzed: true,
            TotalDuration: totalDuration,
            StartSilence: startSilence,
            EndSilence: endSilence,
            InternalSilenceCount: internalCount,
            InternalSilenceTotal: internalTotal,
            ThresholdDb: thresholdDb);
    }
}