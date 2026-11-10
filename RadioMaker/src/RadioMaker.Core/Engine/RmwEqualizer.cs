using System;
using System.Collections.Generic;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Stub de IEqualizer para canales RMW. Guarda las ganancias pero NO las aplica
/// al audio (el EQ de 10 bandas todavía no está integrado con el mixer del Core).
/// </summary>
internal sealed class RmwEqualizer : IEqualizer
{
    private const int Bands = 10;

    private static readonly float[] Frequencies =
        { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

    private readonly double[] _gains;
    private bool _disposed;

    public int BandCount => Bands;
    public IReadOnlyList<float> CenterFrequencies => Frequencies;
    public bool IsEnabled => false;
    public double MinGainDb => -12.0;
    public double MaxGainDb => +12.0;

    public RmwEqualizer() { _gains = new double[Bands]; }

    public void SetEnabled(bool enabled) { _ = enabled; }

    public void SetBandGain(int bandIndex, double gainDb)
    {
        if (bandIndex < 0 || bandIndex >= Bands) throw new ArgumentOutOfRangeException(nameof(bandIndex));
        _gains[bandIndex] = Math.Clamp(gainDb, MinGainDb, MaxGainDb);
    }

    public double GetBandGain(int bandIndex)
    {
        if (bandIndex < 0 || bandIndex >= Bands) throw new ArgumentOutOfRangeException(nameof(bandIndex));
        return _gains[bandIndex];
    }

    public void ApplyGains(IReadOnlyList<double> gainsDb)
    {
        if (_disposed || gainsDb is null) return;
        int count = Math.Min(gainsDb.Count, Bands);
        for (int i = 0; i < count; i++)
            _gains[i] = Math.Clamp(gainsDb[i], MinGainDb, MaxGainDb);
    }

    public void ResetAll() => Array.Clear(_gains, 0, _gains.Length);
    public void Dispose() { _disposed = true; }
}