using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Fx;
using ManagedBass.Mix;
using RMWave.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RMWave.Core.Audio;

/// <summary>
/// Reproductor/mezclador multipista de proyectos .rmw.
///
/// Dos modos de uso:
/// - ownsBass = true  → el player inicializa BASS (Bass.Init) y lo libera en Dispose.
/// - ownsBass = false → el host (RadioMaker) ya inicializó BASS. El player solo
///                      crea su mixer y lo libera. NUNCA toca Bass.Init / Bass.Free.
///
/// INTEGRACIÓN CON RADIOMAKER: se agregaron 3 miembros públicos:
///   - MixerHandle       : handle del mixer (para VU/FFT externos).
///   - SetMasterVolume   : volumen master (0.0 .. 4.0 lineal).
///   - SetMasterGainDb   : helper en dB.
///
/// El EQ de 3 bandas (DXParamEQ) quedó deshabilitado porque no es
/// multiplataforma (solo Windows). Se reemplazará por BASS_FX_BFX_PEAKEQ
/// en una próxima iteración.
/// </summary>
public sealed class TimelinePlayer : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int SchedulerIntervalMs = 15;

    // ═════════════════════════════════════════════════════════════════════
    //  EQ de 3 bandas (Bass / Mid / Treble) con bass_fx PeakEQ
    // ═════════════════════════════════════════════════════════════════════

    // Frecuencias centrales de cada banda.
    private const float EqBassFreq = 100f;
    private const float EqMidFreq = 1000f;
    private const float EqTrebleFreq = 10000f;

    // Ancho de banda (octavas) de cada campana. 2.5 es lo estándar para
    // que las 3 bandas se solapen suavemente sin sonar "quirúrgico".
    private const float EqBandwidth = 2.5f;

    // Límites de ganancia por banda (dB). Coinciden con los knobs de la UI.
    private const float EqMinGainDb = -12f;
    private const float EqMaxGainDb = +12f;

    // CHAN_ALL de bass_fx: aplica la banda a todos los canales del stream.
    private const int EqChannelAll = -1;

    /// <summary>
    /// Replica exacta del struct nativo BASS_BFX_PEAKEQ de bass_fx.h.
    /// Layout: 6 campos de 4 bytes = 24 bytes, sin padding.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BFX_PEAKEQ
    {
        public int lBand;
        public float fBandwidth;
        public float fQ;
        public float fCenter;
        public float fGain;
        public int lChannel;
    }

    /// <summary>
    /// true si bass_fx se cargó correctamente. Se chequea lazy la primera
    /// vez que se intenta aplicar EQ. Si es false, los knobs de EQ no
    /// tienen efecto (comportamiento degradado, sin romper el audio).
    /// </summary>
    private bool? _eqAvailable;

    private readonly bool _ownsBass;
    private readonly object _stateLock = new();

    private int _mixer;
    private volatile bool _initialized;
    private volatile bool _isPlaying;
    private int _disposeState;

    private sealed class ActiveClip
    {
        public Guid ClipId { get; init; }
        public ProjectTrack Track { get; init; } = null!;
        public AudioClip Clip { get; init; } = null!;
        public int Stream { get; init; }
        public double EndTime { get; init; }
    }

    private readonly Dictionary<Guid, ActiveClip> _activeClips = new();
    // Un solo handle de FX por stream (bass_fx usa el mismo handle para
    // todas las bandas; se configuran con FXSetParameters una por una).
    private readonly Dictionary<int, int> _eqFxHandles = new();
    private readonly HashSet<Guid> _failedClips = new();

    private Thread? _schedulerThread;
    private CancellationTokenSource? _schedulerCts;

    public Project Project { get; private set; } = new();
    public bool OwnsBass => _ownsBass;
    public bool IsPlaying => _isPlaying;
    public bool IsInitialized => _initialized;
    public double DurationSeconds => Project.Duration;

    // ===== INTEGRACIÓN CON RADIOMAKER =====
    /// <summary>
    /// Handle del mixer de BASS. El host puede usarlo para ChannelGetLevel / FFT.
    /// IMPORTANTE: el handle PUEDE cambiar tras Play/Stop/Seek.
    /// </summary>
    public int MixerHandle => _mixer;

    /// <summary>
    /// Volumen master del canal (0.0 .. 4.0 lineal, donde 1.0 = sin cambio).
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        if (_mixer == 0) return;
        try { Bass.ChannelSetAttribute(_mixer, ChannelAttribute.Volume, Math.Clamp(volume, 0f, 4f)); }
        catch { }
    }

    /// <summary>
    /// Helper de volumen en dB.
    /// </summary>
    public void SetMasterGainDb(double gainDb)
    {
        if (_mixer == 0) return;
        float linear = (float)Math.Pow(10.0, Math.Clamp(gainDb, -60.0, 12.0) / 20.0);
        SetMasterVolume(linear);
    }

    private double _seekOffset = 0;

    public event Action? PlaybackEnded;

    public TimelinePlayer(bool ownsBass) { _ownsBass = ownsBass; }

    // ==========================================
    // INICIALIZACIÓN
    // ==========================================

    public bool Init()
    {
        lock (_stateLock)
        {
            if (_initialized) return true;
            if (Volatile.Read(ref _disposeState) != 0) return false;

            if (_ownsBass)
            {
                if (!Bass.Init(-1, SampleRate, DeviceInitFlags.Default, IntPtr.Zero))
                    return false;
            }
            else
            {
                try
                {
                    if (Bass.CurrentDevice < 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "TimelinePlayer.Init: ownsBass=false pero Bass.Init no fue llamado por el host.");
                        return false;
                    }
                }
                catch { return false; }
            }

            _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Float | BassFlags.MixerNonStop);
            if (_mixer == 0)
                _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                    BassFlags.Default | BassFlags.MixerNonStop);

            if (_mixer == 0)
            {
                if (_ownsBass) Bass.Free();
                return false;
            }

            _initialized = true;
            return true;
        }
    }

    public bool SetDevice(int deviceId)
    {
        if (!_ownsBass) return false;

        double currentPosition;
        bool wasPlaying;

        StopScheduler();

        lock (_stateLock)
        {
            if (!_initialized) return false;

            wasPlaying = _isPlaying;
            currentPosition = GetPositionSeconds();
            _isPlaying = false;

            if (_mixer != 0) Bass.ChannelStop(_mixer);
            ClearActiveClips();
            _failedClips.Clear();

            if (_mixer != 0) { FreeClipStream(_mixer); _mixer = 0; }

            Bass.Free();

            if (!Bass.Init(deviceId, SampleRate, DeviceInitFlags.Default, IntPtr.Zero))
            {
                _initialized = false;
                return false;
            }

            _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Float | BassFlags.MixerNonStop);
            if (_mixer == 0)
                _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                    BassFlags.Default | BassFlags.MixerNonStop);

            if (_mixer == 0) { Bass.Free(); _initialized = false; return false; }

            RebuildMixerAtPosition(currentPosition);
        }

        if (wasPlaying) Play();
        return true;
    }

    // ==========================================
    // CARGA Y EDICIÓN
    // ==========================================

    public ProjectTrack AddTrack(string? name = null)
    {
        lock (_stateLock)
        {
            var track = new ProjectTrack { Name = name ?? $"Pista {Project.Tracks.Count + 1}" };
            Project.Tracks.Add(track);
            return track;
        }
    }

    public void LoadProject(Project project)
    {
        StopScheduler();

        lock (_stateLock)
        {
            _isPlaying = false;
            if (_mixer != 0) Bass.ChannelStop(_mixer);
            ClearActiveClips();
            _failedClips.Clear();

            if (_mixer != 0) { FreeClipStream(_mixer); _mixer = 0; }

            Project = project ?? new Project();
            _seekOffset = 0;

            if (_initialized)
            {
                _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                    BassFlags.Float | BassFlags.MixerNonStop);
                if (_mixer == 0)
                    _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                        BassFlags.Default | BassFlags.MixerNonStop);
            }
        }
    }

    public AudioClip? AddClip(Guid trackId, string filePath)
    {
        lock (_stateLock)
        {
            var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null) return null;

            var stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (stream == 0) stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode);
            if (stream == 0) return null;

            var fileDuration = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
            Bass.StreamFree(stream);
            if (fileDuration <= 0) return null;

            int row = FindAvailableRow(track, 0, fileDuration);
            if (row >= track.RowCount) track.RowCount = row + 1;

            var clip = new AudioClip
            {
                FilePath = filePath,
                Name = Path.GetFileNameWithoutExtension(filePath),
                StartTime = 0,
                SourceOffset = 0,
                Duration = fileDuration,
                SourceFileDuration = fileDuration,
                Gain = 1.0f,
                Row = row
            };

            track.Clips.Add(clip);
            return clip;
        }
    }

    private int FindAvailableRow(ProjectTrack track, double startTime, double duration)
    {
        double endTime = startTime + duration;
        for (int row = 0; row < track.RowCount; row++)
        {
            bool available = true;
            foreach (var clip in track.Clips)
            {
                if (clip.Row != row) continue;
                if (startTime < clip.EndTime && endTime > clip.StartTime) { available = false; break; }
            }
            if (available) return row;
        }
        return track.RowCount;
    }

    public void RefreshClip(ProjectTrack track, AudioClip clip)
    {
        lock (_stateLock)
        {
            if (!_initialized || _mixer == 0 || track == null || clip == null) return;
            _failedClips.Remove(clip.Id);

            if (_activeClips.TryGetValue(clip.Id, out var active))
            {
                FreeClipStream(active.Stream);
                _activeClips.Remove(clip.Id);
            }

            if (_isPlaying)
            {
                var current = GetPositionSeconds();
                if (current >= clip.StartTime && current < clip.EndTime)
                {
                    int stream = ActivateClip(_mixer, track, clip, current);
                    if (stream != 0)
                        _activeClips[clip.Id] = new ActiveClip
                        {
                            ClipId = clip.Id,
                            Track = track,
                            Clip = clip,
                            Stream = stream,
                            EndTime = clip.EndTime
                        };
                }
            }
        }
    }

    public void UpdateClipAttributes(ProjectTrack track, AudioClip clip)
    {
        lock (_stateLock)
        {
            if (!_initialized || _mixer == 0 || track == null || clip == null) return;

            if (_activeClips.TryGetValue(clip.Id, out var active))
            {
                var volume = GetEffectiveVolume(track, clip);
                Bass.ChannelSetAttribute(active.Stream, ChannelAttribute.Volume, volume);
                Bass.ChannelSetAttribute(active.Stream, ChannelAttribute.Pan, track.Pan);
            }
        }
    }

    // ==========================================
    // TRANSPORTE
    // ==========================================

    public bool Play()
    {
        lock (_stateLock)
        {
            if (Volatile.Read(ref _disposeState) != 0) return false;
            if (!_initialized || _mixer == 0) return false;

            if (Project.Duration > 0 && GetPositionSeconds() >= Project.Duration - 0.05)
                RebuildMixerAtPosition(0);

            UpdateSchedulerInternal();

            if (!Bass.ChannelPlay(_mixer)) return false;
            _isPlaying = true;
        }

        StartScheduler();
        return true;
    }

    public bool Pause()
    {
        lock (_stateLock)
        {
            if (_mixer == 0) return false;
            if (!Bass.ChannelPause(_mixer)) return false;
            _isPlaying = false;
        }
        StopScheduler();
        return true;
    }

    public void Stop()
    {
        StopScheduler();
        lock (_stateLock)
        {
            _isPlaying = false;
            RebuildMixerAtPosition(0);
            _seekOffset = 0;
        }
    }

    public double GetPositionSeconds()
    {
        if (_mixer == 0) return _seekOffset;

        var bytes = Bass.ChannelGetPosition(_mixer, PositionFlags.Bytes);
        if (bytes < 0) return _seekOffset;

        var seconds = Bass.ChannelBytes2Seconds(_mixer, bytes);
        return _seekOffset + (seconds < 0 ? 0 : seconds);
    }

    public void Seek(double seconds)
    {
        StopScheduler();

        bool wasPlaying;
        lock (_stateLock)
        {
            if (!_initialized) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;

            var max = Project.Duration;
            if (max > 0 && seconds > max) seconds = max;

            wasPlaying = _isPlaying;
            _isPlaying = false;

            RebuildMixerAtPosition(seconds);

            if (wasPlaying && _mixer != 0)
            {
                UpdateSchedulerInternal();
                _isPlaying = Bass.ChannelPlay(_mixer);
            }
        }

        if (wasPlaying && _isPlaying) StartScheduler();
    }

    // ==========================================
    // SCHEDULER
    // ==========================================

    private void StartScheduler()
    {
        StopScheduler();

        _schedulerCts = new CancellationTokenSource();
        var token = _schedulerCts.Token;

        _schedulerThread = new Thread(() =>
        {
            try { SchedulerLoop(token); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TimelinePlayer scheduler fatal: {ex}");
            }
        })
        {
            IsBackground = true,
            Name = "TimelinePlayer.Scheduler",
            Priority = ThreadPriority.AboveNormal
        };
        _schedulerThread.Start();
    }

    private void StopScheduler()
    {
        var cts = _schedulerCts;
        var thread = _schedulerThread;
        _schedulerCts = null;
        _schedulerThread = null;

        if (cts != null) { try { cts.Cancel(); } catch { } }

        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            try { thread.Interrupt(); } catch { }
            try { thread.Join(500); } catch { }
        }
    }

    private void SchedulerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool ended = false;

            try
            {
                lock (_stateLock)
                {
                    if (!_isPlaying || Volatile.Read(ref _disposeState) != 0) return;

                    UpdateSchedulerInternal();

                    if (Project.Duration > 0 && GetPositionSeconds() >= Project.Duration - 0.05)
                    {
                        _isPlaying = false;
                        if (_mixer != 0) Bass.ChannelStop(_mixer);
                        ended = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TimelinePlayer scheduler error: {ex.Message}");
            }

            if (ended)
            {
                try { PlaybackEnded?.Invoke(); } catch { }
                return;
            }

            try { Thread.Sleep(SchedulerIntervalMs); }
            catch (ThreadInterruptedException) { return; }
        }
    }

    private void UpdateSchedulerInternal()
    {
        if (_mixer == 0) return;
        var time = GetPositionSeconds();

        foreach (var active in _activeClips.Values.ToList())
        {
            if (time >= active.EndTime)
            {
                FreeClipStream(active.Stream);
                _activeClips.Remove(active.ClipId);
            }
        }

        foreach (var track in Project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (time >= clip.StartTime &&
                    time < clip.EndTime &&
                    !_activeClips.ContainsKey(clip.Id) &&
                    !_failedClips.Contains(clip.Id))
                {
                    int stream = ActivateClip(_mixer, track, clip, time);
                    if (stream != 0)
                    {
                        _activeClips[clip.Id] = new ActiveClip
                        {
                            ClipId = clip.Id,
                            Track = track,
                            Clip = clip,
                            Stream = stream,
                            EndTime = clip.EndTime
                        };
                    }
                    else _failedClips.Add(clip.Id);
                }
            }
        }

        ApplyFades(time);

        if (Bass.ChannelIsActive(_mixer) != PlaybackState.Playing && time < Project.Duration)
            Bass.ChannelPlay(_mixer);
    }

    // ==========================================
    // CONTROL DE PISTAS
    // ==========================================

    public void SetTrackVolume(ProjectTrack track, float volume)
    {
        lock (_stateLock)
        {
            if (track == null) return;
            track.Volume = Math.Clamp(volume, 0f, 1f);
            ApplyActiveAttributes();
        }
    }

    public void SetTrackPan(ProjectTrack track, float pan)
    {
        lock (_stateLock)
        {
            if (track == null) return;
            track.Pan = Math.Clamp(pan, -1f, 1f);
            ApplyActiveAttributes();
        }
    }

    public void SetTrackMute(ProjectTrack track, bool mute)
    {
        lock (_stateLock)
        {
            if (track == null) return;
            track.Mute = mute;
            ApplyActiveAttributes();
        }
    }

    public void SetTrackSolo(ProjectTrack track, bool solo)
    {
        lock (_stateLock)
        {
            if (track == null) return;
            track.Solo = solo;
            ApplyActiveAttributes();
        }
    }

    public void SetTrackEq(ProjectTrack track, float bass, float mid, float treble)
    {
        lock (_stateLock)
        {
            if (track == null) return;

            track.EqBass = Math.Clamp(bass, EqMinGainDb, EqMaxGainDb);
            track.EqMid = Math.Clamp(mid, EqMinGainDb, EqMaxGainDb);
            track.EqTreble = Math.Clamp(treble, EqMinGainDb, EqMaxGainDb);

            // Re-aplicar el EQ a todos los streams activos de esa pista.
            // Si la pista no está sonando ahora, los valores quedan guardados
            // en el modelo y se aplicarán cuando arranque el próximo clip.
            ReapplyEqToActiveStreams(track);
        }
    }

    // ==========================================
    // NORMALIZACIÓN
    // ==========================================

    public float? NormalizeClip(AudioClip clip)
    {
        if (clip == null || string.IsNullOrEmpty(clip.FilePath)) return null;

        int stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode);
        if (stream == 0) return null;

        try
        {
            float peak = 0f;
            long startBytes = Bass.ChannelSeconds2Bytes(stream, clip.SourceOffset);
            long endBytes = Bass.ChannelSeconds2Bytes(stream, clip.SourceOffset + clip.Duration);
            Bass.ChannelSetPosition(stream, startBytes);

            float[] buffer = new float[32768];
            while (Bass.ChannelGetPosition(stream, PositionFlags.Bytes) < endBytes)
            {
                int bytesRead = Bass.ChannelGetData(stream, buffer, buffer.Length * 4);
                if (bytesRead <= 0) break;
                int samples = bytesRead / 4;
                for (int i = 0; i < samples; i++)
                {
                    float v = Math.Abs(buffer[i]);
                    if (v > peak) peak = v;
                }
            }

            Bass.StreamFree(stream);
            if (peak <= 0.0001f) return null;

            float targetLinear = 0.891f;
            float newGain = targetLinear / peak;
            newGain = Math.Clamp(newGain, 0.1f, 10f);

            lock (_stateLock) { clip.Gain = newGain; }
            return newGain;
        }
        catch { Bass.StreamFree(stream); return null; }
    }

    // ==========================================
    // NIVELES
    // ==========================================

    public float GetMasterLevel()
    {
        if (_mixer == 0) return 0;
        try
        {
            int level = Bass.ChannelGetLevel(_mixer);
            if (level == -1) return 0;
            float left = (level & 0xFFFF) / 32768f;
            float right = ((level >> 16) & 0xFFFF) / 32768f;
            return ScaleLevel(Math.Max(left, right));
        }
        catch { }
        return 0;
    }

    public void GetMasterLevelStereo(out float left, out float right)
    {
        left = 0; right = 0;
        if (_mixer == 0) return;

        try
        {
            int level = Bass.ChannelGetLevel(_mixer);
            if (level == -1) return;
            left = (level & 0xFFFF) / 32768f;
            right = ((level >> 16) & 0xFFFF) / 32768f;
        }
        catch { }
    }

    private static float ScaleLevel(float level)
    {
        if (level <= 0f) return 0f;
        float scaled = 1f + 0.5f * (float)Math.Log10(level);
        if (scaled < 0f) return 0f;
        if (scaled > 1f) return 1f;
        return scaled;
    }

    // ==========================================
    // MIXER
    // ==========================================

    private void RebuildMixerAtPosition(double seconds)
    {
        if (_mixer != 0) Bass.ChannelStop(_mixer);
        ClearActiveClips();
        _failedClips.Clear();

        if (_mixer != 0) { FreeClipStream(_mixer); _mixer = 0; }
        _seekOffset = seconds;

        _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
            BassFlags.Float | BassFlags.MixerNonStop);
        if (_mixer == 0)
            _mixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Default | BassFlags.MixerNonStop);

        if (_mixer == 0) return;
        RebuildActiveClips(seconds);
    }

    private void RebuildActiveClips(double time)
    {
        ClearActiveClips();
        foreach (var track in Project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (time >= clip.StartTime && time < clip.EndTime)
                {
                    int stream = ActivateClip(_mixer, track, clip, time);
                    if (stream != 0)
                        _activeClips[clip.Id] = new ActiveClip
                        {
                            ClipId = clip.Id,
                            Track = track,
                            Clip = clip,
                            Stream = stream,
                            EndTime = clip.EndTime
                        };
                    else _failedClips.Add(clip.Id);
                }
            }
        }
    }

    private int ActivateClip(int mixerHandle, ProjectTrack track, AudioClip clip, double time)
    {
        var stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode);
        if (stream == 0) return 0;

        double timeDiff = time - clip.StartTime;
        double sourceSeconds = timeDiff < 0.05 ? clip.SourceOffset : clip.SourceOffset + timeDiff;

        var minSource = clip.SourceOffset;
        var maxSource = clip.SourceOffset + clip.Duration;
        if (sourceSeconds < minSource) sourceSeconds = minSource;
        if (sourceSeconds > maxSource) sourceSeconds = maxSource;

        var bytes = Bass.ChannelSeconds2Bytes(stream, sourceSeconds);
        if (bytes >= 0) Bass.ChannelSetPosition(stream, bytes);

        var baseVolume = GetEffectiveVolume(track, clip);
        float initialVolume = CalculateFadedVolume(track, clip, baseVolume, time);
        Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, initialVolume);
        Bass.ChannelSetAttribute(stream, ChannelAttribute.Pan, track.Pan);

        if (!BassMix.MixerAddChannel(mixerHandle, stream, BassFlags.Default))
        {
            Bass.StreamFree(stream);
            return 0;
        }

        // Aplicar el EQ de 3 bandas al stream (si bass_fx está disponible).
        // Se llama DESPUÉS de agregarlo al mixer, pero ANTES de empezar a
        // sonar (el caller hace MixerAddChannel antes de Play).
        ApplyEqToStream(stream, track);
        return stream;
    }

    private void ClearActiveClips()
    {
        foreach (var active in _activeClips.Values) FreeClipStream(active.Stream);
        _activeClips.Clear();
    }

    private void ApplyActiveAttributes()
    {
        foreach (var active in _activeClips.Values)
        {
            var volume = GetEffectiveVolume(active.Track, active.Clip);
            Bass.ChannelSetAttribute(active.Stream, ChannelAttribute.Volume, volume);
            Bass.ChannelSetAttribute(active.Stream, ChannelAttribute.Pan, active.Track.Pan);
        }
    }

    private void ApplyFades(double currentTime)
    {
        foreach (var active in _activeClips.Values)
        {
            var clip = active.Clip;
            if (clip.FadeIn <= 0 && clip.FadeOut <= 0) continue;

            float baseVolume = GetEffectiveVolume(active.Track, clip);
            float finalVolume = CalculateFadedVolume(active.Track, clip, baseVolume, currentTime);
            Bass.ChannelSetAttribute(active.Stream, ChannelAttribute.Volume, finalVolume);
        }
    }

    private void ApplyRenderFades(Dictionary<Guid, int> activeStreams, double currentTime)
    {
        foreach (var kvp in activeStreams)
        {
            var (clip, track) = FindClipWithTrack(kvp.Key);
            if (clip == null || track == null) continue;
            if (clip.FadeIn <= 0 && clip.FadeOut <= 0) continue;

            float baseVolume = GetEffectiveVolume(track, clip);
            float fadedVolume = CalculateFadedVolume(track, clip, baseVolume, currentTime);
            Bass.ChannelSetAttribute(kvp.Value, ChannelAttribute.Volume, fadedVolume);
        }
    }

    private float GetEffectiveVolume(ProjectTrack track, AudioClip clip)
    {
        var anySolo = Project.Tracks.Any(t => t.Solo);
        if (track.Mute) return 0f;
        if (anySolo && !track.Solo) return 0f;
        var volume = track.Volume * clip.Gain;
        return Math.Clamp(volume, 0f, 1f);
    }

    private float CalculateFadedVolume(ProjectTrack track, AudioClip clip, float baseVolume, double currentTime)
    {
        double clipLocalTime = currentTime - clip.StartTime;
        float finalVolume = baseVolume;

        if (clip.FadeIn > 0 && clipLocalTime < clip.FadeIn)
        {
            double progress = Math.Clamp(clipLocalTime / clip.FadeIn, 0, 1);
            finalVolume = baseVolume * (float)progress;
        }
        else if (clip.FadeOut > 0 && clipLocalTime > clip.Duration - clip.FadeOut)
        {
            double timeLeft = clip.Duration - clipLocalTime;
            double progress = Math.Clamp(timeLeft / clip.FadeOut, 0, 1);
            finalVolume = baseVolume * (float)progress;
        }

        return finalVolume;
    }

    /// <summary>
    /// Aplica (o reemplaza) el EQ de 3 bandas al stream indicado, usando
    /// los valores actuales de la pista. Si ya había un FX asociado al
    /// stream, lo libera primero.
    ///
    /// Devuelve el handle del FX creado, o 0 si bass_fx no está disponible
    /// o la creación falló. En ese caso el stream sigue funcionando sin EQ.
    /// </summary>
    private int ApplyEqToStream(int streamHandle, ProjectTrack track,
                                Dictionary<int, int>? fxHandles = null)
    {
        if (streamHandle == 0 || track == null) return 0;

        var handles = fxHandles ?? _eqFxHandles;

        // Si ya había un FX para este stream, liberarlo primero.
        if (handles.TryGetValue(streamHandle, out var oldFx) && oldFx != 0)
        {
            try { Bass.ChannelRemoveFX(streamHandle, oldFx); } catch { }
            handles.Remove(streamHandle);
        }

        if (!EnsureEqAvailable()) return 0;

        int fxHandle;
        try
        {
            fxHandle = Bass.ChannelSetFX(streamHandle, EffectType.PeakEQ, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EQ] ChannelSetFX exception: {ex.Message}");
            return 0;
        }

        if (fxHandle == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EQ] ChannelSetFX falló. BASS error: {Bass.LastError}");
            return 0;
        }

        handles[streamHandle] = fxHandle;

        // Configurar las 3 bandas.
        ApplyEqBand(fxHandle, band: 0, EqBassFreq, track.EqBass);
        ApplyEqBand(fxHandle, band: 1, EqMidFreq, track.EqMid);
        ApplyEqBand(fxHandle, band: 2, EqTrebleFreq, track.EqTreble);

        return fxHandle;
    }

    /// <summary>
    /// Aplica una banda del EQ con los parámetros nativos de BASS_FX.
    /// </summary>
    private void ApplyEqBand(int fxHandle, int band, float frequency, float gainDb)
    {
        if (fxHandle == 0) return;

        var p = new BFX_PEAKEQ
        {
            lBand = band,
            fBandwidth = EqBandwidth,
            fQ = 0f,
            fCenter = frequency,
            fGain = Math.Clamp(gainDb, EqMinGainDb, EqMaxGainDb),
            lChannel = EqChannelAll,
        };

        var gcHandle = GCHandle.Alloc(p, GCHandleType.Pinned);
        try
        {
            Bass.FXSetParameters(fxHandle, gcHandle.AddrOfPinnedObject());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EQ] FXSetParameters exception: {ex.Message}");
        }
        finally
        {
            gcHandle.Free();
        }
    }

    /// <summary>
    /// Re-aplica el EQ a todos los clips activos de la pista indicada.
    /// Se llama cuando el usuario mueve un knob de EQ en vivo.
    ///
    /// Si un clip no tiene FX todavía (porque se activó con EQ en bypass
    /// por algún error), ApplyEqToStream lo va a crear en este momento.
    /// </summary>
    private void ReapplyEqToActiveStreams(ProjectTrack track)
    {
        if (!EnsureEqAvailable()) return;

        foreach (var active in _activeClips.Values)
        {
            if (!ReferenceEquals(active.Track, track)) continue;
            if (active.Stream == 0) continue;

            // Si ya tiene FX, actualizar parámetros sin recrear.
            if (_eqFxHandles.TryGetValue(active.Stream, out var fxHandle) && fxHandle != 0)
            {
                ApplyEqBand(fxHandle, 0, EqBassFreq, track.EqBass);
                ApplyEqBand(fxHandle, 1, EqMidFreq, track.EqMid);
                ApplyEqBand(fxHandle, 2, EqTrebleFreq, track.EqTreble);
            }
            else
            {
                ApplyEqToStream(active.Stream, track);
            }
        }
    }

    /// <summary>
    /// Chequea (una sola vez) si bass_fx está disponible. Si no, los
    /// knobs de EQ no tienen efecto pero el audio sigue funcionando.
    /// </summary>
    private bool EnsureEqAvailable()
    {
        if (_eqAvailable.HasValue) return _eqAvailable.Value;

        try
        {
            // BassFx.Version lanza excepción si la DLL nativa no está.
            _ = BassFx.Version;
            _eqAvailable = true;
        }
        catch
        {
            _eqAvailable = false;
            System.Diagnostics.Debug.WriteLine(
                "[EQ] bass_fx no disponible. Los knobs de EQ no tendrán efecto.");
        }

        return _eqAvailable.Value;
    }

    private void FreeClipStream(int streamHandle)
    {
        if (streamHandle == 0) return;

        // Liberar el FX de EQ asociado (si lo hay). BASS StreamFree ya
        // libera los FX automáticamente, pero lo hacemos explícito para
        // evitar handles huérfanos en el dict.
        if (_eqFxHandles.TryGetValue(streamHandle, out var fxHandle) && fxHandle != 0)
        {
            try { Bass.ChannelRemoveFX(streamHandle, fxHandle); } catch { }
            _eqFxHandles.Remove(streamHandle);
        }

        Bass.StreamFree(streamHandle);
    }

    // ==========================================
    // RENDER OFFLINE
    // ==========================================

    public async Task RenderToFileAsync(string outputPath,
        Action<double> progressCallback, Action<string> statusCallback)
    {
        await Task.Run(() =>
        {
            int renderMixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Decode | BassFlags.MixerNonStop);
            if (renderMixer == 0) { statusCallback("Error creando mixer de renderizado."); return; }

            var mixInfo = Bass.ChannelGetInfo(renderMixer);
            System.Diagnostics.Debug.WriteLine(
                $"[Render WAV] Mixer: freq={mixInfo.Frequency}, chans={mixInfo.Channels}, flags={mixInfo.Flags}");

            int keepAliveStream = CreateSilentKeepAliveStream();
            if (keepAliveStream != 0)
            {
                if (!BassMix.MixerAddChannel(renderMixer, keepAliveStream, BassFlags.Default))
                {
                    System.Diagnostics.Debug.WriteLine($"[Render] Keep-alive add FAILED: {Bass.LastError}");
                    Bass.StreamFree(keepAliveStream);
                    keepAliveStream = 0;
                }
                else Bass.ChannelSetAttribute(keepAliveStream, ChannelAttribute.Volume, 0f);
            }

            var activeStreams = new Dictionary<Guid, int>();
            var renderEqHandles = new Dictionary<int, int>();
            double duration = Project.Duration;
            int totalActivations = 0;

            if (duration <= 0)
            {
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                Bass.StreamFree(renderMixer);
                statusCallback("El proyecto está vacío.");
                return;
            }

            try
            {
                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var writer = new BinaryWriter(fs);

                writer.Write("RIFF".ToCharArray()); writer.Write(0); writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray()); writer.Write(16); writer.Write((short)1);
                writer.Write((short)Channels); writer.Write(SampleRate);
                writer.Write(SampleRate * Channels * 2); writer.Write((short)(Channels * 2));
                writer.Write((short)16); writer.Write("data".ToCharArray()); writer.Write(0);

                int bufferSize = 65536;
                byte[] byteBuffer = new byte[bufferSize];
                double currentTime = 0;

                while (currentTime < duration)
                {
                    foreach (var kvp in activeStreams.ToList())
                    {
                        var clip = FindClip(kvp.Key);
                        if (clip == null || currentTime >= clip.EndTime)
                        {
                            FreeClipStreamOffline(kvp.Value, renderEqHandles);
                            activeStreams.Remove(kvp.Key);
                        }
                    }

                    foreach (var track in Project.Tracks)
                    {
                        foreach (var clip in track.Clips)
                        {
                            if (currentTime >= clip.StartTime &&
                                currentTime < clip.EndTime &&
                                !activeStreams.ContainsKey(clip.Id))
                            {
                                int stream = ActivateClipOffline(renderMixer, track, clip,
                                    currentTime, renderEqHandles);
                                if (stream != 0) { activeStreams[clip.Id] = stream; totalActivations++; }
                            }
                        }
                    }

                    ApplyRenderFades(activeStreams, currentTime);

                    int bytesRead = Bass.ChannelGetData(renderMixer, byteBuffer, bufferSize);
                    if (bytesRead <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Render WAV] Stall en t={currentTime:F2}s / {duration:F2}s");
                        break;
                    }

                    fs.Write(byteBuffer, 0, bytesRead);
                    double secondsRead = Bass.ChannelBytes2Seconds(renderMixer, bytesRead);
                    currentTime += secondsRead;
                    progressCallback(Math.Min(1.0, currentTime / duration));
                }

                long dataLength = fs.Length - 44;
                fs.Seek(4, SeekOrigin.Begin); writer.Write((int)(fs.Length - 8));
                fs.Seek(40, SeekOrigin.Begin); writer.Write((int)dataLength);

                statusCallback("Exportación completada con éxito.");
            }
            catch (Exception ex) { statusCallback($"Error exportando: {ex.Message}"); }
            finally
            {
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                foreach (var stream in activeStreams.Values) FreeClipStreamOffline(stream, renderEqHandles);
                Bass.StreamFree(renderMixer);
                System.Diagnostics.Debug.WriteLine(
                    $"[Render WAV] Activaciones: {totalActivations} / {Project.Tracks.Sum(t => t.Clips.Count)} clips.");
            }
        });
    }

    public async Task RenderToMp3Async(string outputPath,
        Action<double> progressCallback, Action<string> statusCallback)
    {
        await Task.Run(() =>
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                statusCallback($"Error: El directorio no existe: {outputDir}");
                return;
            }

            int renderMixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Decode | BassFlags.MixerNonStop);
            if (renderMixer == 0) { statusCallback("Error creando mixer de renderizado."); return; }

            int keepAliveStream = CreateSilentKeepAliveStream();
            if (keepAliveStream != 0)
            {
                if (!BassMix.MixerAddChannel(renderMixer, keepAliveStream, BassFlags.Default))
                {
                    Bass.StreamFree(keepAliveStream);
                    keepAliveStream = 0;
                }
                else Bass.ChannelSetAttribute(keepAliveStream, ChannelAttribute.Volume, 0f);
            }

            var activeStreams = new Dictionary<Guid, int>();
            var renderEqHandles = new Dictionary<int, int>();
            double duration = Project.Duration;
            int totalActivations = 0;

            if (duration <= 0)
            {
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                Bass.StreamFree(renderMixer);
                statusCallback("El proyecto está vacío.");
                return;
            }

            int encoder = 0;
            try
            {
                encoder = BassEnc_Mp3.Start(renderMixer, null, EncodeFlags.Default, outputPath);
                if (encoder == 0)
                {
                    var error = Bass.LastError;
                    Bass.StreamFree(renderMixer);
                    statusCallback($"Error iniciando encoder MP3 (código: {error}).");
                    return;
                }

                int bufferSize = 32768;
                byte[] buffer = new byte[bufferSize];
                var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                IntPtr bufferPtr = gcHandle.AddrOfPinnedObject();
                double currentTime = 0;

                try
                {
                    while (currentTime < duration)
                    {
                        foreach (var kvp in activeStreams.ToList())
                        {
                            var clip = FindClip(kvp.Key);
                            if (clip == null || currentTime >= clip.EndTime)
                            {
                                FreeClipStreamOffline(kvp.Value, renderEqHandles);
                                activeStreams.Remove(kvp.Key);
                            }
                        }

                        foreach (var track in Project.Tracks)
                        {
                            foreach (var clip in track.Clips)
                            {
                                if (currentTime >= clip.StartTime &&
                                    currentTime < clip.EndTime &&
                                    !activeStreams.ContainsKey(clip.Id))
                                {
                                    int stream = ActivateClipOffline(renderMixer, track, clip,
                                        currentTime, renderEqHandles);
                                    if (stream != 0) { activeStreams[clip.Id] = stream; totalActivations++; }
                                }
                            }
                        }

                        ApplyRenderFades(activeStreams, currentTime);

                        int bytesRead = Bass.ChannelGetData(renderMixer, bufferPtr, bufferSize);
                        if (bytesRead <= 0) break;

                        double secondsRead = Bass.ChannelBytes2Seconds(renderMixer, bytesRead);
                        currentTime += secondsRead;
                        progressCallback(Math.Min(1.0, currentTime / duration));
                    }
                }
                finally { gcHandle.Free(); }

                BassEnc.EncodeStop(encoder);
                encoder = 0;

                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    statusCallback($"Exportación MP3 completada. Tamaño: {fileInfo.Length / 1024} KB.");
                }
                else statusCallback("Advertencia: El archivo no fue creado.");
            }
            catch (Exception ex) { statusCallback($"Error exportando MP3: {ex.Message}"); }
            finally
            {
                if (encoder != 0) BassEnc.EncodeStop(encoder);
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                foreach (var stream in activeStreams.Values) FreeClipStreamOffline(stream, renderEqHandles);
                Bass.StreamFree(renderMixer);
            }
        });
    }

    public async Task<bool> RenderRangeToFileAsync(string outputPath, double startTime, double endTime,
        Action<double> progressCallback, Action<string> statusCallback)
    {
        if (endTime <= startTime) { statusCallback("Rango inválido."); return false; }

        bool success = false;
        await Task.Run(() =>
        {
            int renderMixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Decode | BassFlags.MixerNonStop);
            if (renderMixer == 0) { statusCallback("Error creando mixer de renderizado."); return; }

            int keepAliveStream = CreateSilentKeepAliveStream();
            if (keepAliveStream != 0)
            {
                if (!BassMix.MixerAddChannel(renderMixer, keepAliveStream, BassFlags.Default))
                {
                    Bass.StreamFree(keepAliveStream); keepAliveStream = 0;
                }
                else Bass.ChannelSetAttribute(keepAliveStream, ChannelAttribute.Volume, 0f);
            }

            var activeStreams = new Dictionary<Guid, int>();
            var renderEqHandles = new Dictionary<int, int>();
            double rangeDuration = endTime - startTime;
            int totalActivations = 0;

            try
            {
                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var writer = new BinaryWriter(fs);

                writer.Write("RIFF".ToCharArray()); writer.Write(0); writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray()); writer.Write(16); writer.Write((short)1);
                writer.Write((short)Channels); writer.Write(SampleRate);
                writer.Write(SampleRate * Channels * 2); writer.Write((short)(Channels * 2));
                writer.Write((short)16); writer.Write("data".ToCharArray()); writer.Write(0);

                int maxBufferSize = 65536;
                byte[] byteBuffer = new byte[maxBufferSize];
                double currentTime = startTime;

                while (currentTime < endTime)
                {
                    foreach (var kvp in activeStreams.ToList())
                    {
                        var clip = FindClip(kvp.Key);
                        if (clip == null || currentTime >= clip.EndTime)
                        {
                            FreeClipStreamOffline(kvp.Value, renderEqHandles);
                            activeStreams.Remove(kvp.Key);
                        }
                    }

                    foreach (var track in Project.Tracks)
                    {
                        foreach (var clip in track.Clips)
                        {
                            if (currentTime >= clip.StartTime &&
                                currentTime < clip.EndTime &&
                                !activeStreams.ContainsKey(clip.Id))
                            {
                                int stream = ActivateClipOffline(renderMixer, track, clip,
                                    currentTime, renderEqHandles);
                                if (stream != 0) { activeStreams[clip.Id] = stream; totalActivations++; }
                            }
                        }
                    }

                    ApplyRenderFades(activeStreams, currentTime);

                    double remainingTime = endTime - currentTime;
                    int bytesToRead = (int)(remainingTime * SampleRate * Channels * 2);
                    if (bytesToRead > maxBufferSize) bytesToRead = maxBufferSize;
                    if (bytesToRead <= 0) break;

                    int bytesRead = Bass.ChannelGetData(renderMixer, byteBuffer, bytesToRead);
                    if (bytesRead <= 0) break;

                    fs.Write(byteBuffer, 0, bytesRead);
                    double secondsRead = Bass.ChannelBytes2Seconds(renderMixer, bytesRead);
                    currentTime += secondsRead;
                    progressCallback(Math.Min(1.0, (currentTime - startTime) / rangeDuration));
                }

                long dataLength = fs.Length - 44;
                fs.Seek(4, SeekOrigin.Begin); writer.Write((int)(fs.Length - 8));
                fs.Seek(40, SeekOrigin.Begin); writer.Write((int)dataLength);

                statusCallback("Exportación de selección completada.");
                success = true;
            }
            catch (Exception ex) { statusCallback($"Error exportando selección: {ex.Message}"); }
            finally
            {
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                foreach (var stream in activeStreams.Values) FreeClipStreamOffline(stream, renderEqHandles);
                Bass.StreamFree(renderMixer);
            }
        });
        return success;
    }

    public async Task<bool> RenderRangeToMp3Async(string outputPath, double startTime, double endTime,
        Action<double> progressCallback, Action<string> statusCallback)
    {
        if (endTime <= startTime) { statusCallback("Rango inválido."); return false; }

        bool success = false;
        await Task.Run(() =>
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                statusCallback($"Error: El directorio no existe: {outputDir}");
                return;
            }

            int renderMixer = BassMix.CreateMixerStream(SampleRate, Channels,
                BassFlags.Decode | BassFlags.MixerNonStop);
            if (renderMixer == 0) { statusCallback("Error creando mixer de renderizado."); return; }

            int keepAliveStream = CreateSilentKeepAliveStream();
            if (keepAliveStream != 0)
            {
                if (!BassMix.MixerAddChannel(renderMixer, keepAliveStream, BassFlags.Default))
                {
                    Bass.StreamFree(keepAliveStream); keepAliveStream = 0;
                }
                else Bass.ChannelSetAttribute(keepAliveStream, ChannelAttribute.Volume, 0f);
            }

            var activeStreams = new Dictionary<Guid, int>();
            var renderEqHandles = new Dictionary<int, int>();
            double rangeDuration = endTime - startTime;
            int encoder = 0;
            int totalActivations = 0;

            try
            {
                encoder = BassEnc_Mp3.Start(renderMixer, null, EncodeFlags.Default, outputPath);
                if (encoder == 0)
                {
                    var error = Bass.LastError;
                    Bass.StreamFree(renderMixer);
                    statusCallback($"Error iniciando encoder MP3 (código: {error}).");
                    return;
                }

                int maxBufferSize = 32768;
                byte[] buffer = new byte[maxBufferSize];
                var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                IntPtr bufferPtr = gcHandle.AddrOfPinnedObject();
                double currentTime = startTime;

                try
                {
                    while (currentTime < endTime)
                    {
                        foreach (var kvp in activeStreams.ToList())
                        {
                            var clip = FindClip(kvp.Key);
                            if (clip == null || currentTime >= clip.EndTime)
                            {
                                FreeClipStreamOffline(kvp.Value, renderEqHandles);
                                activeStreams.Remove(kvp.Key);
                            }
                        }

                        foreach (var track in Project.Tracks)
                        {
                            foreach (var clip in track.Clips)
                            {
                                if (currentTime >= clip.StartTime &&
                                    currentTime < clip.EndTime &&
                                    !activeStreams.ContainsKey(clip.Id))
                                {
                                    int stream = ActivateClipOffline(renderMixer, track, clip,
                                        currentTime, renderEqHandles);
                                    if (stream != 0) { activeStreams[clip.Id] = stream; totalActivations++; }
                                }
                            }
                        }

                        ApplyRenderFades(activeStreams, currentTime);

                        double remainingTime = endTime - currentTime;
                        int bytesToRead = (int)(remainingTime * SampleRate * Channels * 2);
                        if (bytesToRead > maxBufferSize) bytesToRead = maxBufferSize;
                        if (bytesToRead <= 0) break;

                        int bytesRead = Bass.ChannelGetData(renderMixer, bufferPtr, bytesToRead);
                        if (bytesRead <= 0) break;

                        double secondsRead = Bass.ChannelBytes2Seconds(renderMixer, bytesRead);
                        currentTime += secondsRead;
                        progressCallback(Math.Min(1.0, (currentTime - startTime) / rangeDuration));
                    }
                }
                finally { gcHandle.Free(); }

                BassEnc.EncodeStop(encoder);
                encoder = 0;

                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    statusCallback($"Exportación MP3 completada. Tamaño: {fileInfo.Length / 1024} KB.");
                    success = true;
                }
                else statusCallback("Advertencia: El archivo no fue creado.");
            }
            catch (Exception ex) { statusCallback($"Error exportando selección MP3: {ex.Message}"); }
            finally
            {
                if (encoder != 0) BassEnc.EncodeStop(encoder);
                if (keepAliveStream != 0) { BassMix.MixerRemoveChannel(keepAliveStream); Bass.StreamFree(keepAliveStream); }
                foreach (var stream in activeStreams.Values) FreeClipStreamOffline(stream, renderEqHandles);
                Bass.StreamFree(renderMixer);
            }
        });
        return success;
    }

    private void FreeClipStreamOffline(int streamHandle, Dictionary<int, int> eqHandles)
    {
        if (streamHandle == 0) return;

        if (eqHandles.TryGetValue(streamHandle, out var fxHandle) && fxHandle != 0)
        {
            try { Bass.ChannelRemoveFX(streamHandle, fxHandle); } catch { }
            eqHandles.Remove(streamHandle);
        }

        Bass.StreamFree(streamHandle);
    }

    private int ActivateClipOffline(int mixerHandle, ProjectTrack track, AudioClip clip,
                                    double time, Dictionary<int, int> eqHandles)
    {
        var stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) stream = Bass.CreateStream(clip.FilePath, 0, 0, BassFlags.Decode);
        if (stream == 0) return 0;

        double timeDiff = time - clip.StartTime;
        double sourceSeconds = timeDiff < 0.05 ? clip.SourceOffset : clip.SourceOffset + timeDiff;

        var minSource = clip.SourceOffset;
        var maxSource = clip.SourceOffset + clip.Duration;
        if (sourceSeconds < minSource) sourceSeconds = minSource;
        if (sourceSeconds > maxSource) sourceSeconds = maxSource;

        var bytes = Bass.ChannelSeconds2Bytes(stream, sourceSeconds);
        if (bytes >= 0) Bass.ChannelSetPosition(stream, bytes);

        var baseVolume = GetEffectiveVolume(track, clip);
        float initialVolume = CalculateFadedVolume(track, clip, baseVolume, time);
        Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, initialVolume);
        Bass.ChannelSetAttribute(stream, ChannelAttribute.Pan, track.Pan);

        if (!BassMix.MixerAddChannel(mixerHandle, stream, BassFlags.Default))
        {
            var info = Bass.ChannelGetInfo(mixerHandle);
            System.Diagnostics.Debug.WriteLine(
                $"[ActivateClipOffline] MixerAddChannel FAILED: err={Bass.LastError}, mixerFreq={info.Frequency}");
            Bass.StreamFree(stream);
            return 0;
        }

        // Aplicar EQ también en render offline (el mix debe sonar igual
        // que en playback en vivo). Si bass_fx no está, sigue sin EQ.
        ApplyEqToStream(stream, track, eqHandles);
        return stream;
    }

    private static int CreateSilentKeepAliveStream()
    {
        try
        {
            byte[] zeros = new byte[65536];

            StreamProcedure silenceProc = (handle, buffer, length, user) =>
            {
                int remaining = length;
                IntPtr ptr = buffer;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, zeros.Length);
                    Marshal.Copy(zeros, 0, ptr, chunk);
                    ptr += chunk;
                    remaining -= chunk;
                }
                return length;
            };

            return Bass.CreateStream(SampleRate, Channels,
                BassFlags.Decode | BassFlags.Loop, silenceProc, IntPtr.Zero);
        }
        catch { return 0; }
    }

    private AudioClip? FindClip(Guid id)
    {
        foreach (var t in Project.Tracks)
            foreach (var c in t.Clips)
                if (c.Id == id) return c;
        return null;
    }

    private (AudioClip? Clip, ProjectTrack? Track) FindClipWithTrack(Guid id)
    {
        foreach (var t in Project.Tracks)
            foreach (var c in t.Clips)
                if (c.Id == id) return (c, t);
        return (null, null);
    }

    // ==========================================
    // DISPOSE
    // ==========================================

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        StopScheduler();

        lock (_stateLock)
        {
            _isPlaying = false;
            if (_mixer != 0) Bass.ChannelStop(_mixer);
            ClearActiveClips();

            if (_mixer != 0)
            {
                FreeClipStream(_mixer);
                _mixer = 0;
                _eqFxHandles.Clear();
            }

            if (_ownsBass && _initialized) Bass.Free();
            _initialized = false;
        }
    }
}