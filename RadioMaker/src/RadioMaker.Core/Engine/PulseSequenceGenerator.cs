using System;
using System.Runtime.InteropServices;
using ManagedBass;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Genera una secuencia de N pulsos senoidales con gaps de silencio.
/// Se usa para el beep de emergencia.
///
/// Estrategia: en lugar de trackear un contador de ciclo (que puede
/// desincronizarse si BASS entrega buffers de tamaño variable), calculamos
/// el indice global del frame desde el inicio del stream y derivamos
/// isInPulse con un modulo. Sin estado interno fragil.
/// </summary>
internal sealed class PulseSequenceGenerator
{
    private readonly StreamProcedure _procedure;
    private readonly double _frequencyHz;
    private readonly int _sampleRate;
    private readonly double _amplitude;
    private readonly int _pulseFrames;
    private readonly int _gapFrames;
    private readonly int _cycleFrames;
    private readonly int _totalFrames;

    private int _remainingFrames;
    private double _phase;

    private short[] _samplesBuffer = Array.Empty<short>();

    public PulseSequenceGenerator(
        double frequencyHz,
        int sampleRate,
        int pulseMs,
        int gapMs,
        int pulseCount,
        double amplitude)
    {
        _frequencyHz = frequencyHz;
        _sampleRate = sampleRate;
        _amplitude = Math.Clamp(amplitude, 0.0, 1.0);

        _pulseFrames = Math.Max(1, sampleRate * Math.Max(10, pulseMs) / 1000);
        _gapFrames = Math.Max(0, sampleRate * Math.Max(0, gapMs) / 1000);
        _cycleFrames = _pulseFrames + _gapFrames;

        int count = Math.Max(1, pulseCount);
        _totalFrames = _pulseFrames * count + _gapFrames * (count - 1);
        _remainingFrames = _totalFrames;

        _procedure = WriteSamples;
    }

    public int CreateStream()
    {
        return Bass.CreateStream(_sampleRate, 2, BassFlags.Default, _procedure, IntPtr.Zero);
    }

    private int WriteSamples(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int totalFrames = length / 4; // 4 bytes por frame (16-bit estereo)
        int framesToWrite = Math.Min(totalFrames, _remainingFrames);
        if (framesToWrite <= 0) return 0;

        int totalSamples = framesToWrite * 2;
        if (_samplesBuffer.Length < totalSamples)
            _samplesBuffer = new short[totalSamples];

        double delta = 2.0 * Math.PI * _frequencyHz / _sampleRate;
        double maxAmp = short.MaxValue * _amplitude;

        // Indice global del frame desde el inicio del stream completo.
        int frameIdxFromStart = _totalFrames - _remainingFrames;

        for (int i = 0; i < framesToWrite; i++)
        {
            int frameIdx = frameIdxFromStart + i;
            int cycleIdx = _cycleFrames > 0 ? frameIdx % _cycleFrames : 0;
            bool isInPulse = _pulseFrames > 0 && cycleIdx < _pulseFrames;

            short s = 0;
            if (isInPulse)
            {
                s = (short)(maxAmp * Math.Sin(_phase));
                _phase += delta;
                if (_phase >= 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
            }
            else
            {
                // Al entrar al gap, resetear la fase para que el proximo
                // pulso empiece en fase 0 (evita clicks).
                _phase = 0.0;
            }

            _samplesBuffer[i * 2] = s;
            _samplesBuffer[i * 2 + 1] = s;
        }

        Marshal.Copy(_samplesBuffer, 0, buffer, totalSamples);
        _remainingFrames -= framesToWrite;
        return framesToWrite * 4;
    }
}