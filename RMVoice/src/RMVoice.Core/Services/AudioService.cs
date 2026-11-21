using ManagedBass;
using RMVoice.Core.Models;
using System.IO;
using System.Diagnostics;

namespace RMVoice.Core.Services;

/// -------------
/// Motor de reproducción basado en ManagedBass.
///
/// Reglas de diseño:
///  • Solo una locución a la vez en toda la app (SemaphoreSlim(1,1)).
///  • El callback de fin de región NO llama a BASS: solo completa un TaskCompletionSource.
///  • El hilo que espera el TCS es quien detiene el canal al terminar la región.
///    (Sin esto, el canal sigue reproduciendo más allá del fin de la región.)
///  • El device y el volumen se aplican a cada stream ANTES de cada segmento.
///  • Si el servicio no es dueño de BASS, NO llama a Init/Free.
/// -------------
public sealed class AudioService : IDisposable
{
    private readonly SyncProcedure _endSyncProc;
    private readonly Dictionary<TrackKind, int> _streams = new();
    private readonly Dictionary<TrackKind, int> _syncs = new();
    private readonly object _gate = new();

    // Serializa las locuciones: una sola a la vez.
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private int _stopGeneration;
    private int _currentStreamHandle;

    // TCS del segmento en curso (lo completa el callback o Stop).
    private TaskCompletionSource<bool>? _currentSegmentTcs;

    private float _volume = 1f;
    private bool _initialized;
    private bool _disposed;

    // ---------- DEBUG TEMPORAL ----------
    private static readonly bool DebugLog = true;   // poner en false cuando terminemos
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RMVoice", "audio_debug.log");

    private static void Log(string msg)
    {
        if (!DebugLog) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
    // -----------------------------------

    public AudioService(bool ownsBass = true)
    {
        OwnsBass = ownsBass;
        _endSyncProc = OnEndSync;

        foreach (TrackKind kind in Enum.GetValues(typeof(TrackKind)))
        {
            _streams[kind] = 0;
            _syncs[kind] = 0;
        }
    }

    /// -------------
    /// <c>true</c>  → este servicio es dueño de BASS (Init/Free).
    /// <c>false</c> → el host (RadioMaker) ya lo inicializó.
    /// -------------
    public bool OwnsBass { get; }

    public bool IsInitialized => _initialized;

    /// -------------True si al menos una pista está cargada.
    public bool IsReady => _streams.Values.Any(h => h != 0);

    /// -------------True si hay una locución reproduciéndose ahora mismo.
    public bool IsPlaying => _playLock.CurrentCount == 0;

    /// -------------Handle BASS del segmento de voz actual, o cero si no hay audio activo.
    public int CurrentStreamHandle => Volatile.Read(ref _currentStreamHandle);

    /// -------------Dispositivo por defecto (se usa cuando el request no especifica uno).
    public int DefaultDevice { get; set; } = -1;

    /// -------------Volumen global del Engine (0.0 – 1.0).
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            ApplyVolumeToAllStreams();
        }
    }

    // ---------------------------------------------------------------------
    //  Inicialización
    // ---------------------------------------------------------------------

    public void Init()
    {
        if (_initialized) return;

        // Precargar las DLLs nativas de BASS ANTES de que ManagedBass las
        // intente cargar. Sin esto, `Bass.Init()` falla con DllNotFoundException
        // cuando el engine corre standalone (RMVoice.exe sin RadioMaker).
        try { NativeLibraryLoader.EnsureLoaded(); } catch { }

        if (OwnsBass)
        {
            if (!Bass.Init())
                throw new InvalidOperationException(
                    $"No se pudo inicializar BASS (último error: {Bass.LastError}). " +
                    "Comprueba que exista un dispositivo de audio disponible.");
        }

        _initialized = true;
    }

    // ---------------------------------------------------------------------
    //  Carga / descarga de pistas
    // ---------------------------------------------------------------------

    public bool HasTrack(TrackKind kind) =>
        _streams.TryGetValue(kind, out var h) && h != 0;

    public void LoadTrack(TrackKind kind, string audioPath)
    {
        UnloadTrack(kind);
        var handle = CreateStream(audioPath);
        _streams[kind] = handle;
        _syncs[kind] = 0;
    }

    public void UnloadTrack(TrackKind kind)
    {
        if (!_streams.TryGetValue(kind, out var stream) || stream == 0)
            return;

        if (_syncs.TryGetValue(kind, out var sync) && sync != 0)
        {
            Bass.ChannelRemoveSync(stream, sync);
            _syncs[kind] = 0;
        }

        Bass.StreamFree(stream);
        _streams[kind] = 0;
    }

    private int CreateStream(string audioPath)
    {
        Init();

        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("La ruta del audio está vacía.", nameof(audioPath));

        if (!File.Exists(audioPath))
            throw new FileNotFoundException("No se encontró el archivo de audio.", audioPath);

        var handle = Bass.CreateStream(audioPath, 0, 0,BassFlags.Prescan);

        if (handle == 0)
            throw new InvalidOperationException(
                $"BASS no pudo abrir «{Path.GetFileName(audioPath)}» (error: {Bass.LastError}). " +
                "Si el archivo es MP3/OGG/FLAC necesitas añadir el plugin correspondiente de BASS.");

        Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, _volume);

        // Buffer de 50 ms: reduce al mínimo la "cola" que BASS arrastra al seek.
        Bass.ChannelSetAttribute(handle, ChannelAttribute.Buffer, 50);

        return handle;
    }

    // ---------------------------------------------------------------------
    //  Reproducción por cola de segmentos
    // ---------------------------------------------------------------------

    /// -------------
    /// Reproduce los segmentos en orden. Devuelve cuando el último segmento terminó.
    /// Si ya hay otra locución en curso, espera su turno.
    /// -------------
    public async Task PlaySequenceAsync(
        IEnumerable<Segment> segments,
        int deviceIndex,
        float volume,
        CancellationToken ct = default)
    {
        var list = segments.ToList();
        if (list.Count == 0) return;

        int stopGeneration = Volatile.Read(ref _stopGeneration);
        // Esperar a que termine cualquier locución previa.
        await _playLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (Volatile.Read(ref _stopGeneration) != stopGeneration)
                return;

            ApplyDeviceAndVolume(deviceIndex, volume);

            foreach (var segment in list)
            {
                ct.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _stopGeneration) != stopGeneration)
                    break;

                if (!_streams.TryGetValue(segment.Kind, out var stream) || stream == 0)
                    continue;   // pista no cargada: saltar

                if (!await PlaySegmentAsync(segment, stream, ct).ConfigureAwait(false))
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _currentStreamHandle, 0);
            _playLock.Release();
        }
    }

    /// -------------Corta la locución en curso (si la hay). El bloqueo del llamador se completa.
    public void Stop()
    {
        Interlocked.Increment(ref _stopGeneration);
        Interlocked.Exchange(ref _currentStreamHandle, 0);
        var tcs = Interlocked.Exchange(ref _currentSegmentTcs, null);

        lock (_gate)
        {
            foreach (var kv in _streams)
            {
                if (kv.Value == 0) continue;

                if (_syncs.TryGetValue(kv.Key, out var sync) && sync != 0)
                {
                    Bass.ChannelRemoveSync(kv.Value, sync);
                    _syncs[kv.Key] = 0;
                }

                Bass.ChannelStop(kv.Value);
            }
        }

        tcs?.TrySetResult(false);
    }

    /// -------------
    /// Reproduce un único segmento: programa el sync de fin, hace seek al inicio
    /// de la región, arranca, espera el fin, y DETIENE el canal.
    /// -------------
    private async Task<bool> PlaySegmentAsync(Segment segment, int stream, CancellationToken ct)
    {
        var endBytes = Bass.ChannelSeconds2Bytes(stream, segment.Region.EndSeconds);
        var startBytes = Bass.ChannelSeconds2Bytes(stream, segment.Region.StartSeconds);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _currentSegmentTcs, tcs);

        lock (_gate)
        {
            // 1. Limpiar sync viejo si quedó alguno.
            if (_syncs.TryGetValue(segment.Kind, out var old) && old != 0)
            {
                Bass.ChannelRemoveSync(stream, old);
                _syncs[segment.Kind] = 0;
            }

            // 2. Detener el canal y forzar posición 0 (descarta el buffer).
            Bass.ChannelStop(stream);
            Bass.ChannelSetPosition(stream, 0, PositionFlags.Bytes);

            // 3. Programar el sync de fin.
            var sync = Bass.ChannelSetSync(
                stream,
                SyncFlags.Position | SyncFlags.Onetime,
                endBytes,
                _endSyncProc);

            _syncs[segment.Kind] = sync;

            Interlocked.Exchange(ref _currentStreamHandle, stream);

            // 4. play.
            Bass.ChannelPlay(stream, false);

            // 5. Seek al inicio de la región (canal detenido, posición limpia).
            Bass.ChannelSetPosition(stream, startBytes, PositionFlags.Bytes);

        }

        using var reg = ct.Register(() =>
        {
            var current = Interlocked.Exchange(ref _currentSegmentTcs, null);
            current?.TrySetCanceled(ct);
        });

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _syncs[segment.Kind] = 0;
                Bass.ChannelStop(stream);
            }
        }
    }

    /// -------------
    /// Callback de fin de región. Corre en el hilo interno de BASS.
    /// NO llamar a funciones de BASS aquí: solo completar el TCS.
    /// -------------
    private void OnEndSync(int handle, int channel, int data, IntPtr user)
    {
        var tcs = Interlocked.Exchange(ref _currentSegmentTcs, null);
        tcs?.TrySetResult(true);
    }

    private void ApplyDeviceAndVolume(int deviceIndex, float volume)
    {
        var vol = Math.Clamp(volume, 0f, 1f);

        lock (_gate)
        {
            foreach (var kv in _streams)
            {
                if (kv.Value == 0) continue;

                if (deviceIndex >= 0)
                    Bass.ChannelSetDevice(kv.Value, deviceIndex);

                Bass.ChannelSetAttribute(kv.Value, ChannelAttribute.Volume, vol);
            }
        }
    }

    private void ApplyVolumeToAllStreams()
    {
        lock (_gate)
        {
            foreach (var kv in _streams)
            {
                if (kv.Value != 0)
                    Bass.ChannelSetAttribute(kv.Value, ChannelAttribute.Volume, _volume);
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Dispose
    // ---------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        foreach (TrackKind kind in Enum.GetValues(typeof(TrackKind)))
            UnloadTrack(kind);

        if (_initialized && OwnsBass)
            Bass.Free();

        _initialized = false;
        _playLock.Dispose();
    }
}
