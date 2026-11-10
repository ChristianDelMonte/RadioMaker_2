using ManagedBass;
using System;
using System.Collections.Generic;

namespace RMWave.Core.Audio;

public class WaveformCache
{
    private readonly Dictionary<string, float[]> _cache = new();
    private readonly object _lock = new();

    public float[]? GetPeaks(string path)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var peaks))
                return peaks;
        }
        return null;
    }

    public void CachePeaks(string path, float[] peaks)
    {
        lock (_lock)
        {
            _cache[path] = peaks;
        }
    }

    public float[]? CalculatePeaks(string path)
    {
        try
        {
            int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (stream == 0)
            {
                stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode);
                if (stream == 0) return null;
            }

            var info = Bass.ChannelGetInfo(stream);
            int channels = info.Channels;
            long lengthBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
            double lengthSeconds = Bass.ChannelBytes2Seconds(stream, lengthBytes);

            if (lengthSeconds <= 0 || channels <= 0)
            {
                Bass.StreamFree(stream);
                return null;
            }

            int targetPeaks = (int)Math.Max(1000, lengthSeconds * 100);
            int bytesPerSample = 4;
            int frameBytes = bytesPerSample * channels;

            long bytesPerPeak = lengthBytes / targetPeaks;
            bytesPerPeak = (bytesPerPeak / frameBytes) * frameBytes;
            if (bytesPerPeak < frameBytes) bytesPerPeak = frameBytes;

            int actualPeaks = (int)(lengthBytes / bytesPerPeak);
            if (actualPeaks <= 0)
            {
                Bass.StreamFree(stream);
                return null;
            }

            float[] peaks = new float[actualPeaks];
            int bufferSize = 65536;
            float[] buffer = new float[bufferSize];

            Bass.ChannelSetPosition(stream, 0);

            int currentPeakIndex = 0;
            long bytesAccumulated = 0;
            float currentMax = 0;
            long nextBoundary = bytesPerPeak;

            while (true)
            {
                int bytesObtained = Bass.ChannelGetData(stream, buffer, bufferSize * bytesPerSample);
                if (bytesObtained <= 0) break;

                int samplesObtained = bytesObtained / bytesPerSample;

                for (int i = 0; i < samplesObtained; i++)
                {
                    float val = Math.Abs(buffer[i]);
                    if (val > currentMax) currentMax = val;

                    bytesAccumulated += bytesPerSample;

                    if (bytesAccumulated >= nextBoundary)
                    {
                        if (currentPeakIndex < actualPeaks)
                        {
                            peaks[currentPeakIndex] = currentMax;
                            currentPeakIndex++;
                        }
                        currentMax = 0;
                        nextBoundary += bytesPerPeak;

                        if (currentPeakIndex >= actualPeaks) break;
                    }
                }

                if (currentPeakIndex >= actualPeaks) break;
            }

            Bass.StreamFree(stream);
            return peaks;
        }
        catch
        {
            return null;
        }
    }
}