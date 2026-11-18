using ManagedBass;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RMWave.Core.Audio;

/// <summary>
/// Información de un dispositivo de grabación disponible.
/// </summary>
public class RecordingDeviceInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsLoopback { get; set; }

    public override string ToString() =>
        IsLoopback ? $"Loopback: {Name}" : Name;
}

/// <summary>
/// Motor de grabación de audio usando BASS Record.
/// Graba en memoria y escribe WAV al detener (como el ejemplo oficial rectest.c).
/// </summary>
public class RecordingEngine : IDisposable
{
    private int _recordChannel = 0;
    private bool _deviceInitialized = false;
    private bool _disposed = false;

    // Buffer de grabación en memoria.
    private MemoryStream? _recordStream = new MemoryStream();
    private readonly object _streamLock = new object();

    // Guardar referencia al delegate para evitar que el GC lo elimine.
    private readonly RecordProcedure _recordProc;

    public bool IsRecording => _recordChannel != 0;
    public string OutputFilePath { get; private set; } = "";
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }

    public RecordingEngine()
    {
        // Callback de grabación: copia los datos al buffer en memoria.
        // Se ejecuta en el hilo de grabación de BASS.
        _recordProc = (handle, buffer, length, user) =>
        {
            if (length > 0)
            {
                byte[] data = new byte[length];
                Marshal.Copy(buffer, data, 0, length);

                lock (_streamLock)
                {
                    // El stream puede liberarse entre callbacks; comprobarlo
                    // dentro del mismo lock que protege su disposición.
                    _recordStream?.Write(data, 0, length);
                }
            }
            return true; // continuar grabando
        };
    }

    /// <summary>
    /// Enumera todos los dispositivos de grabación disponibles.
    /// </summary>
    public static List<RecordingDeviceInfo> GetRecordingDevices()
    {
        var devices = new List<RecordingDeviceInfo>();

        for (int i = 0; Bass.RecordGetDeviceInfo(i, out var info); i++)
        {
            devices.Add(new RecordingDeviceInfo
            {
                Id = i,
                Name = info.Name ?? $"Dispositivo {i}",
                IsLoopback = info.IsLoopback
            });
        }

        return devices;
    }

    /// <summary>
    /// Inicializa un dispositivo de grabación.
    /// Libera el dispositivo anterior si había uno.
    /// </summary>
    public bool InitDevice(int deviceId)
    {
        if (IsRecording)
            StopRecording();

        if (_deviceInitialized)
        {
            Bass.RecordFree();
            _deviceInitialized = false;
        }

        _deviceInitialized = Bass.RecordInit(deviceId);
        return _deviceInitialized;
    }

    /// <summary>
    /// Inicia la grabación en memoria.
    /// </summary>
    public bool StartRecording(int sampleRate, int channels, string outputPath)
    {
        if (!_deviceInitialized) return false;
        if (IsRecording) return false;

        SampleRate = sampleRate;
        Channels = channels;
        OutputFilePath = outputPath;

        // Resetear el buffer.
        lock (_streamLock)
        {
            _recordStream?.Dispose();
            _recordStream = new MemoryStream();
        }

        // Iniciar grabación PAUSADA primero, para evitar perder los primeros
        // milisegundos entre el RecordStart y el ChannelPlay.
        _recordChannel = Bass.RecordStart(sampleRate, channels, BassFlags.RecordPause, _recordProc, IntPtr.Zero);
        if (_recordChannel == 0) return false;

        // Reanudar la grabación.
        Bass.ChannelPlay(_recordChannel);

        return true;
    }

    /// <summary>
    /// Detiene la grabación y escribe el archivo WAV.
    /// </summary>
    public void StopRecording()
    {
        if (_recordChannel != 0)
        {
            Bass.ChannelStop(_recordChannel);
            // Importante: liberar el canal de grabación para no filtrar el handle
            // en cada grabación. BASS_StreamFree es válido para canales de record.
            Bass.StreamFree(_recordChannel);
            _recordChannel = 0;
        }

        // Escribir el WAV al archivo.
        lock (_streamLock)
        {
            if (_recordStream is { Length: > 0 } recordingStream &&
                !string.IsNullOrEmpty(OutputFilePath))
            {
                WriteWavFile(OutputFilePath, recordingStream);
            }
        }
    }

    /// <summary>
    /// Escribe el buffer de grabación como un archivo WAV PCM 16-bit.
    /// Mismo formato que el ejemplo oficial rectest.c.
    /// </summary>
    private void WriteWavFile(string path, MemoryStream recordingStream)
    {
        try
        {
            int dataSize = (int)recordingStream.Length;
            int byteRate = SampleRate * Channels * 2;
            short blockAlign = (short)(Channels * 2);

            using var fs = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            // RIFF header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);        // PCM
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);       // bits por muestra

            // data chunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            writer.Write(recordingStream.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error escribiendo WAV: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtiene el nivel actual de la grabación (0..1).
    /// Usa el mismo método que el ejemplo AudioLevelMonitor de RadioBot.
    /// </summary>
    public float GetRecordingLevel()
    {
        if (_recordChannel == 0) return 0;

        try
        {
            int level = Bass.ChannelGetLevel(_recordChannel);
            if (level == -1) return 0;

            int leftRaw = level & 0xFFFF;
            int rightRaw = (level >> 16) & 0xFFFF;
            float left = leftRaw / 32768f;
            float right = rightRaw / 32768f;
            return Math.Clamp(Math.Max(left, right), 0f, 1f);
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Obtiene la duración grabada hasta el momento (en segundos).
    /// </summary>
    public double GetRecordedDuration()
    {
        long length;
        lock (_streamLock)
        {
            if (_recordStream == null || _recordStream.Length == 0) return 0;
            length = _recordStream.Length;
        }

        int bytesPerSecond = SampleRate * Channels * 2; // 16-bit
        if (bytesPerSecond == 0) return 0;

        return (double)length / bytesPerSecond;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopRecording();

        lock (_streamLock)
        {
            _recordStream?.Dispose();
            _recordStream = null;
        }

        if (_deviceInitialized)
        {
            Bass.RecordFree();
            _deviceInitialized = false;
        }
    }
}
