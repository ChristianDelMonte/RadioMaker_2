using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// -------------
/// Implementacion de IEqualizer usando el addon bass_fx (BASS_FX_BFX_PEAKEQ).
/// Cross-platform: Windows / Linux / macOS.
///
/// Estrategia (identica al ejemplo oficial dsp.c de Un4seen):
///   1. UN solo ChannelSetFX con EffectType.PeakEQ, priority 0.
///   2. Para cada banda: FXSetParameters con el struct, distinto lBand (0..9).
///   3. Al cambiar una ganancia: FXSetParameters solo de esa banda.
///
/// Usamos un struct LOCAL con solo tipos primitivos para evitar el error
/// "Object contains references" que da la clase PeakEQ del wrapper (que
/// intenta pinnear una clase con referencias managed).
/// -------------
internal sealed class BassEqualizer : IEqualizer
{
    private static readonly float[] DefaultFrequencies =
        { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

    private const double MinGain = -12.0;
    private const double MaxGain = +12.0;
    private const float DefaultBandwidth = 2.5f;
    private const int ChannelAll = -1;

    /// -------------
    /// Replica exacta del struct nativo BASS_BFX_PEAKEQ de bass_fx.h.
    /// Layout: 6 campos de 4 bytes = 24 bytes, sin padding.
    /// -------------
    [StructLayout(LayoutKind.Sequential)]
    private struct BfxPeakEq
    {
        public int lBand;
        public float fBandwidth;
        public float fQ;
        public float fCenter;
        public float fGain;
        public int lChannel;
    }

    private readonly int _channelHandle;
    private readonly double[] _gains;

    private int _fxHandle;
    private bool _enabled;
    private bool _disposed;

    public int BandCount => DefaultFrequencies.Length;
    public IReadOnlyList<float> CenterFrequencies => DefaultFrequencies;
    public bool IsEnabled => _enabled;
    public double MinGainDb => MinGain;
    public double MaxGainDb => MaxGain;

    public BassEqualizer(int channelHandle)
    {
        _channelHandle = channelHandle;
        _gains = new double[DefaultFrequencies.Length];
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        if (_enabled == enabled) return;

        if (enabled)
        {
            CreateFx();
            ApplyAllBands();
        }
        else
        {
            RemoveFx();
        }

        _enabled = enabled;
    }

    public void SetBandGain(int bandIndex, double gainDb)
    {
        if (_disposed) return;
        if (bandIndex < 0 || bandIndex >= BandCount)
            throw new ArgumentOutOfRangeException(nameof(bandIndex));

        _gains[bandIndex] = Math.Clamp(gainDb, MinGain, MaxGain);

        if (_enabled && _fxHandle != 0)
            ApplyBand(bandIndex);
    }

    public double GetBandGain(int bandIndex)
    {
        if (bandIndex < 0 || bandIndex >= BandCount)
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        return _gains[bandIndex];
    }

    public void ApplyGains(IReadOnlyList<double> gainsDb)
    {
        if (_disposed) return;

        int count = Math.Min(gainsDb.Count, BandCount);
        for (int i = 0; i < count; i++)
            _gains[i] = Math.Clamp(gainsDb[i], MinGain, MaxGain);

        if (_enabled && _fxHandle != 0)
            ApplyAllBands();
    }

    public void ResetAll() => ApplyGains(new double[BandCount]);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveFx();
    }

    // ---------------------------------------------------------------------

    private void CreateFx()
    {
        if (_fxHandle != 0) return;

        _fxHandle = Bass.ChannelSetFX(
            _channelHandle,
            EffectType.PeakEQ,
            0);

        if (_fxHandle == 0)
        {
            var err = Bass.LastError;
            throw new BassEngineException(
                "No se pudo crear el FX de EQ. " +
                "Verifica que bass_fx este cargado (BassFx.Version debe funcionar).",
                err);
        }
    }

    private void RemoveFx()
    {
        if (_fxHandle == 0) return;

        try { Bass.ChannelRemoveFX(_channelHandle, _fxHandle); }
        catch { }

        _fxHandle = 0;
    }

    private void ApplyAllBands()
    {
        for (int i = 0; i < BandCount; i++)
            ApplyBand(i);
    }

    private void ApplyBand(int band)
    {
        if (_fxHandle == 0) return;

        var p = new BfxPeakEq
        {
            lBand = band,
            fBandwidth = DefaultBandwidth,
            fQ = 0f,
            fCenter = DefaultFrequencies[band],
            fGain = (float)_gains[band],
            lChannel = ChannelAll
        };

        var gcHandle = GCHandle.Alloc(p, GCHandleType.Pinned);
        try
        {
            Bass.FXSetParameters(_fxHandle, gcHandle.AddrOfPinnedObject());
        }
        finally
        {
            gcHandle.Free();
        }
    }
}
