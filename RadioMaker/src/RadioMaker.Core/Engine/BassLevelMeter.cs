using ManagedBass;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Implementación de <see cref="IAudioLevelMeter"/> basada en
/// <c>Bass.ChannelGetLevel</c>.
/// </summary>
internal sealed class BassLevelMeter : IAudioLevelMeter
{
    private readonly int _handle;
    private readonly object _readGate = new();
    private readonly float[] _peakBuffer = new float[2];
    private readonly float[] _rmsBuffer = new float[2];

    public BassLevelMeter(int handle)
    {
        _handle = handle;
    }

    public AudioLevels Read()
    {
        if (_handle == 0) return AudioLevels.Silent;

        lock (_readGate)
        {
            try
            {
                if (!Bass.ChannelGetLevel(_handle, _peakBuffer, 0.05f, LevelRetrievalFlags.Stereo))
                    return AudioLevels.Silent;

                if (!Bass.ChannelGetLevel(
                        _handle,
                        _rmsBuffer,
                        0.05f,
                        LevelRetrievalFlags.Stereo | LevelRetrievalFlags.RMS))
                    return AudioLevels.Silent;

                double l = Math.Clamp(_peakBuffer[0], 0.0, 1.0);
                double r = Math.Clamp(_peakBuffer[1], 0.0, 1.0);
                double rmsL = Math.Clamp(_rmsBuffer[0], 0.0, 1.0);
                double rmsR = Math.Clamp(_rmsBuffer[1], 0.0, 1.0);

                bool clipL = _peakBuffer[0] >= 0.995f;
                bool clipR = _peakBuffer[1] >= 0.995f;

                return new AudioLevels(l, r, rmsL, rmsR, clipL, clipR);
            }
            catch
            {
                return AudioLevels.Silent;
            }
        }
    }
}
