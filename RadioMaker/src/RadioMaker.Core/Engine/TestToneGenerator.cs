using System.Runtime.InteropServices;
using ManagedBass;

// (namespace renombrado — ver nota en BassEngine.cs)
namespace RadioMaker.Core.Engine;

/// -------------
/// Genera un tono senoidal PCM de 16 bits estéreo que BASS puede reproducir
/// como si fuera un stream de archivo. Se usa para el botón "Probar salida".
///
/// Notas:
///   • El buffer que llega al StreamProcedure está en BYTES, no en samples.
///   • 16-bit estéreo → 4 bytes por frame (2 bytes por canal × 2 canales).
///   • Devolver 0 en el callback señala a BASS que el stream terminó.
///   • Escribimos las muestras en un <c>short[]</c> managed reutilizable y las
///     copiamos al buffer nativo con <see cref="Marshal.Copy(short[], int, IntPtr, int)"/>.
///     Así evitamos bloques unsafe y el flag /unsafe en el .csproj.
/// -------------
internal sealed class TestToneGenerator
{
    private readonly StreamProcedure _procedure;
    private readonly double _frequencyHz;
    private readonly int _sampleRate;
    private int _samplesRemaining;
    private double _phase;

    // Buffer managed reutilizable — se redimensiona solo si hace falta.
    // Bajo condiciones normales el tamaño máximo lo determina el buffer que
    // BASS nos pasa en cada callback (típicamente unos pocos KB).
    private short[] _samplesBuffer = Array.Empty<short>();

    public TestToneGenerator(double frequencyHz, int sampleRate, int durationMs)
    {
        _frequencyHz = frequencyHz;
        _sampleRate = sampleRate;
        _samplesRemaining = (int)((long)sampleRate * durationMs / 1000L);
        _procedure = WriteSamples;
    }

    /// -------------
    /// Crea el stream de BASS asociado. El handle devuelto debe liberarse con
    /// <c>Bass.StreamFree</c> una vez finalizada la reproducción.
    /// -------------
    public int CreateStream()
    {
        // Bass.CreateStream(freq, channels, flags, procedure, user)
        return Bass.CreateStream(
            _sampleRate,
            2,
            BassFlags.Default,
            _procedure,
            IntPtr.Zero);
    }

    private int WriteSamples(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int totalFrames = length / 4; // 4 bytes por frame (16-bit estéreo)
        int framesToWrite = Math.Min(totalFrames, _samplesRemaining);

        if (framesToWrite <= 0)
            return 0; // fin de stream

        int totalSamples = framesToWrite * 2; // 2 canales

        // Asegurar capacidad del buffer reutilizable.
        if (_samplesBuffer.Length < totalSamples)
            _samplesBuffer = new short[totalSamples];

        double delta = 2.0 * Math.PI * _frequencyHz / _sampleRate;
        const double amplitude = short.MaxValue * 0.25; // -12 dBFS aprox., tono suave

        for (int i = 0; i < framesToWrite; i++)
        {
            short s = (short)(amplitude * Math.Sin(_phase));
            _samplesBuffer[i * 2]     = s;   // L
            _samplesBuffer[i * 2 + 1] = s;   // R

            _phase += delta;
            if (_phase >= 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
        }

        // Copiar las muestras al buffer nativo que BASS nos entregó.
        // Marshal.Copy(short[] source, int startIndex, IntPtr destination, int length)
        Marshal.Copy(_samplesBuffer, 0, buffer, totalSamples);

        _samplesRemaining -= framesToWrite;
        return framesToWrite * 4;
    }
}
