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
    private readonly float[] _buffer = new float[2];

    public BassLevelMeter(int handle)
    {
        _handle = handle;
    }

    public AudioLevels Read()
    {
        if (_handle == 0) return AudioLevels.Silent;

        try
        {
            if (!Bass.ChannelGetLevel(_handle, _buffer, 0.05f, LevelRetrievalFlags.Stereo))
                return AudioLevels.Silent;

            double l = Math.Clamp(_buffer[0], 0.0, 1.0);
            double r = Math.Clamp(_buffer[1], 0.0, 1.0);

            bool clipL = _buffer[0] >= 0.995f;
            bool clipR = _buffer[1] >= 0.995f;

            return new AudioLevels(l, r, l, r, clipL, clipR);
        }
        catch
        {
            return AudioLevels.Silent;
        }
    }
}
