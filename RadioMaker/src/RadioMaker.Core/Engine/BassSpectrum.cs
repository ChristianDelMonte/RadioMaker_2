using System.Runtime.InteropServices;
using ManagedBass;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Implementacion de <see cref="IAudioSpectrum"/> sobre un handle de BASS.
///
/// Usa BASS_ChannelGetData con el flag BASS_DATA_FFT2048 para obtener los
/// datos de FFT (1024 bins de magnitud). Los valores ya vienen normalizados
/// en el rango 0..1 aprox., donde los agudos suelen tener valores mas bajos
/// que los graves (por eso el ejemplo oficial aplica sqrt() para mejorar la
/// visibilidad).
///
/// Tambien permite leer muestras crudas del canal para dibujar la forma de
/// onda (waveform), como en el modo 3 del ejemplo spectrum.c.
///
/// NO es thread-safe con la liberacion del canal: el llamador debe dejar
/// de consultarlo ANTES de disponer el canal.
/// </summary>
internal sealed class BassSpectrum : IAudioSpectrum
{
    /// <summary>Cantidad de bins de FFT que devuelve BASS_DATA_FFT2048.</summary>
    public const int FftBins = 1024;

    private readonly int _handle;
    private readonly int _channels;
    private readonly int _sampleRate;

    // Buffer reutilizable para las muestras crudas.
    private float[] _waveBuffer = Array.Empty<float>();

    public int ChannelCount => _channels;
    public int FftBinCount => FftBins;
    public int SampleRate => _sampleRate;

    internal BassSpectrum(int handle)
    {
        _handle = handle;

        // Intentamos leer informacion del canal.
        int channels = 2;
        int sampleRate = 44100;

        try
        {
            if (Bass.ChannelGetInfo(handle, out var info))
            {
                channels = info.Channels > 0 ? info.Channels : 2;
                sampleRate = info.Frequency > 0 ? info.Frequency : 44100;
            }
        }
        catch { }

        _channels = channels;
        _sampleRate = sampleRate;
    }

    public bool ReadFft(float[] buffer)
    {
        if (_handle == 0) return false;
        if (buffer is null || buffer.Length < FftBins) return false;

        try
        {
            // BASS_ChannelGetData con BASS_DATA_FFT2048 devuelve 1024 floats
            // (mitad de 2048). Los pines de ManagedBass permiten pasar un
            // float[] directamente.
            int bytesRead = Bass.ChannelGetData(_handle, buffer, (int)DataFlags.FFT2048);

            return bytesRead > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool ReadWaveform(float[] buffer, int frameCount)
    {
        if (_handle == 0) return false;
        if (buffer is null) return false;
        if (frameCount <= 0) return false;

        int needed = frameCount * _channels;
        if (buffer.Length < needed) return false;

        try
        {
            // Para leer muestras crudas como float hay que pedir
            // BASS_DATA_FLOAT | BASS_DATA_FFT_NOWINDOW (no, sin ventana).
            // El modo correcto es BASS_DATA_FLOAT, que devuelve los samples
            // float del canal sin procesar.
            // La cantidad de bytes a leer = frames * channels * sizeof(float).
            int bytes = needed * sizeof(float);

            int readBytes = Bass.ChannelGetData(
                _handle,
                buffer,
                bytes | (int)DataFlags.Float);

            return readBytes > 0;
        }
        catch
        {
            return false;
        }
    }
}