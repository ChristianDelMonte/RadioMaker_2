using System;
using System.IO;
using System.Threading;
using ManagedBass;
using RMWave.Core.Audio;
using RadioMaker.Core.Abstractions;
using PlaybackState = RadioMaker.Core.Abstractions.PlaybackState;

namespace RadioMaker.Core.Engine;

/// -------------
/// IAudioChannel sobre un TimelinePlayer del Core de RMWave.
/// Permite reproducir un .rmw como si fuera un archivo de audio en RadioMaker.
/// -------------
internal sealed class RmwChannel : IAudioChannel
{
    private readonly TimelinePlayer _player;
    private readonly string _busId;
    private readonly string _filePath;
    private readonly RmwEqualizer _equalizer;
    private readonly Action<RmwChannel>? _onDisposed;

    // Analizadores: se recrean on-demand porque el mixer handle cambia
    // después de Play/Stop/Seek (el Core lo recrea internamente).
    private BassLevelMeter? _meter;
    private BassSpectrum? _spectrum;
    private int _meterHandle = -1;
    private int _spectrumHandle = -1;

    private double _userVolume = 1.0;
    private double _busVolume;
    private double _normGainDb = 0.0;
    private double _duckingGainDb = 0.0;
    private double _globalVolume;

    private TimeSpan _trimStart = TimeSpan.Zero;
    private TimeSpan _trimEnd = TimeSpan.Zero;

    private bool _autoFadeEnabled;
    private double _autoFadeSeconds;

    private bool _disposed;
    private bool _userStopped;
    private int _endEventRaised;
    private readonly object _fadeGate = new();
    private Timer? _fadeOutCompletionTimer;
    private Timer? _fadeOutPauseCompletionTimer;

    private Timer? _trimEndTimer;
    private Timer? _autoFadeOutTimer;
    private Timer? _nearEndTimer;
    private double _crossfadeSeconds;

    public event EventHandler<ChannelEndedEventArgs>? Ended;
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler? NearEnd;

    public RmwChannel(
        string filePath,
        string busId,
        double busVolume,
        double globalVolume,
        Action<RmwChannel>? onDisposed = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Se requiere una ruta de archivo.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Proyecto RMW no encontrado.", filePath);

        var project = RmwLoader.Load(filePath)
            ?? throw new InvalidOperationException($"No se pudo parsear: {Path.GetFileName(filePath)}");

        if (project.Tracks.Count == 0)
            throw new InvalidOperationException($"Proyecto sin pistas: {Path.GetFileName(filePath)}");
        if (project.Duration <= 0)
            throw new InvalidOperationException($"Proyecto vacío (duración 0): {Path.GetFileName(filePath)}");

        _filePath = filePath;
        _busId = busId;
        _busVolume = Math.Clamp(busVolume, 0.0, 1.0);
        _globalVolume = Math.Clamp(globalVolume, 0.0, 1.0);
        _onDisposed = onDisposed;

        _player = new TimelinePlayer(ownsBass: false);
        if (!_player.Init())
        {
            _player.Dispose();
            throw new InvalidOperationException(
                $"No se pudo inicializar TimelinePlayer para '{Path.GetFileName(filePath)}'.");
        }

        _player.LoadProject(project);
        _player.PlaybackEnded += OnPlaybackEnded;

        _equalizer = new RmwEqualizer();
        ApplyCombinedVolume();
    }

    public string BusId => _busId;
    public int Handle => _player.MixerHandle;
    public string? FilePath => _filePath;

    public IAudioLevelMeter Meter
    {
        get
        {
            int h = _player.MixerHandle;
            if (_meter is null || _meterHandle != h) { _meter = new BassLevelMeter(h); _meterHandle = h; }
            return _meter;
        }
    }

    public IEqualizer Equalizer => _equalizer;

    public IAudioSpectrum Spectrum
    {
        get
        {
            int h = _player.MixerHandle;
            if (_spectrum is null || _spectrumHandle != h) { _spectrum = new BassSpectrum(h); _spectrumHandle = h; }
            return _spectrum;
        }
    }

    public PlaybackState State
    {
        get
        {
            if (_disposed) return PlaybackState.Stopped;
            int h = _player.MixerHandle;
            if (h == 0) return PlaybackState.Stopped;

            try
            {
                return Bass.ChannelIsActive(h) switch
                {
                    ManagedBass.PlaybackState.Playing => PlaybackState.Playing,
                    ManagedBass.PlaybackState.Paused => PlaybackState.Paused,
                    ManagedBass.PlaybackState.Stalled => PlaybackState.Stalled,
                    _ => PlaybackState.Stopped
                };
            }
            catch { return PlaybackState.Stopped; }
        }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds(_player.DurationSeconds);
    public TimeSpan Position => TimeSpan.FromSeconds(_player.GetPositionSeconds());
    public TimeSpan TrimStart => _trimStart;
    public TimeSpan TrimEnd => _trimEnd;

    public TimeSpan EffectiveDuration
    {
        get
        {
            var t = Duration - _trimStart - _trimEnd;
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }
    }

    public double NormalizationGainDb => _normGainDb;
    public bool IsAutoFadeEnabled => _autoFadeEnabled;
    public double AutoFadeSeconds => _autoFadeSeconds;
    public double DuckingGainDb => _duckingGainDb;

    public bool Play()
    {
        if (_disposed) return false;
        Interlocked.Exchange(ref _endEventRaised, 0);
        _userStopped = false;

        if (_trimStart > TimeSpan.Zero)
        {
            try { _player.Seek(_trimStart.TotalSeconds); } catch { }
        }
        else if (Duration > TimeSpan.Zero && Position >= Duration - TimeSpan.FromSeconds(0.05))
        {
            // TimelinePlayer.Play reinicia desde cero al llegar al final y
            // recrea el mixer. Anticipar el seek evita aplicar el fade al
            // handle antiguo justo antes de esa recreación.
            try { _player.Seek(0); } catch { }
        }

        if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
        {
            try
            {
                int h = _player.MixerHandle;
                if (h != 0) Bass.ChannelSetAttribute(h, ChannelAttribute.Volume, 0f);
            }
            catch { }
        }

        bool ok;
        try { ok = _player.Play(); }
        catch { ok = false; }
        if (!ok) return false;

        if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
        {
            try
            {
                int h = _player.MixerHandle;
                if (h != 0)
                {
                    float target = (float)ComputeCombinedVolume();
                    int ms = (int)(_autoFadeSeconds * 1000);
                    Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, target, ms);
                }
            }
            catch { }
        }
        else ApplyCombinedVolume();

        StartTrimEndTimer();
        StartAutoFadeOutTimer();
        RaiseState(PlaybackState.Playing);
        return true;
    }

    public bool Pause()
    {
        if (_disposed) return false;
        bool ok;
        try { ok = _player.Pause(); }
        catch { ok = false; }

        if (ok)
        {
            StopTrimEndTimer();
            StopAutoFadeOutTimer();
            RaiseState(PlaybackState.Paused);
        }
        return ok;
    }

    public bool Stop()
    {
        if (_disposed) return false;
        bool wasActive = State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stalled;
        CancelPendingFadeCompletions();
        _userStopped = true;
        StopTrimEndTimer();
        StopAutoFadeOutTimer();
        StopNearEndTimer();
        try { _player.Stop(); } catch { }
        ApplyCombinedVolume();
        RaiseState(PlaybackState.Stopped);
        if (wasActive) NotifyEnded(wasStoppedByUser: true);
        return true;
    }

    public bool Seek(TimeSpan position)
    {
        if (_disposed) return false;
        try
        {
            _player.Seek(Math.Max(0, position.TotalSeconds));
            ApplyCombinedVolume();
            return true;
        }
        catch { return false; }
    }

    public bool SetVolume(double volume)
    {
        _userVolume = Math.Clamp(volume, 0.0, 1.0);
        return ApplyCombinedVolume();
    }

    public void SetGlobalVolume(double volume)
    {
        double clamped = Math.Clamp(volume, 0.0, 1.0);
        if (Math.Abs(clamped - _globalVolume) < 0.001) return;
        _globalVolume = clamped;
        ApplyCombinedVolume();
    }

    public void SetTrim(TimeSpan trimStart, TimeSpan trimEnd)
    {
        if (trimStart < TimeSpan.Zero) trimStart = TimeSpan.Zero;
        if (trimEnd < TimeSpan.Zero) trimEnd = TimeSpan.Zero;
        _trimStart = trimStart;
        _trimEnd = trimEnd;

        // NOTA: no hacemos seek al trimStart aca. El seek inicial al
        // trimStart lo maneja Play() antes de arrancar el canal. Si el
        // analisis llega tarde (SetTrim durante playback), no hacemos
        // seek BACK porque se percibe como restart.

        if (State == PlaybackState.Playing) StartTrimEndTimer();
        if (_crossfadeSeconds > 0.05) StartNearEndTimer();
    }

    public void ClearTrim()
    {
        _trimStart = TimeSpan.Zero;
        _trimEnd = TimeSpan.Zero;
        StopTrimEndTimer();
        if (_crossfadeSeconds > 0.05) StartNearEndTimer();
    }

    public void SetNormalizationGain(double gainDb)
    {
        _normGainDb = Math.Clamp(gainDb, -60.0, 60.0);
        ApplyCombinedVolume();
    }

    public void ClearNormalization() { _normGainDb = 0.0; ApplyCombinedVolume(); }

    public void SetDuckingGain(double gainDb)
    {
        double clamped = Math.Clamp(gainDb, -60.0, 0.0);
        if (Math.Abs(clamped - _duckingGainDb) < 0.05) return;
        _duckingGainDb = clamped;
        ApplyCombinedVolume();
    }

    public void SetAutoFade(bool enabled, double fadeSeconds)
    {
        _autoFadeEnabled = enabled;
        _autoFadeSeconds = Math.Clamp(fadeSeconds, 0.0, 30.0);
        if (_autoFadeEnabled && State == PlaybackState.Playing) StartAutoFadeOutTimer();
        else if (!_autoFadeEnabled) StopAutoFadeOutTimer();
    }

    public bool FadeIn(int durationMs = 1000, double targetVolume = 1.0, bool preservePosition = false)
    {
        if (_disposed) return false;
        CancelPendingFadeCompletions();
        _userVolume = Math.Clamp(targetVolume, 0.0, 1.0);

        bool needsStart = State != PlaybackState.Playing;
        if (needsStart)
            Interlocked.Exchange(ref _endEventRaised, 0);
        if (needsStart)
        {
            // Play() reinicia el timeline si ya estaba al final. Hacer ese
            // seek aquí fuerza cualquier recreación del mixer antes de
            // capturar el handle sobre el que se programa el fade.
            try
            {
                if (Duration > TimeSpan.Zero && Position >= Duration - TimeSpan.FromSeconds(0.05))
                    _player.Seek(_trimStart.TotalSeconds);
                else if (!preservePosition && _trimStart > TimeSpan.Zero)
                    _player.Seek(_trimStart.TotalSeconds);
            }
            catch { }
        }

        int h = _player.MixerHandle;
        if (h == 0) return false;

        float target = (float)ComputeCombinedVolume();
        try
        {
            Bass.ChannelSetAttribute(h, ChannelAttribute.Volume, 0f);
            bool ok = Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, target, Math.Max(1, durationMs));

            if (ok && needsStart)
            {
                ok = _player.Play() && ok;
                if (ok) { StartTrimEndTimer(); StartAutoFadeOutTimer(); }
            }

            if (ok) RaiseState(PlaybackState.Playing);
            return ok;
        }
        catch { return false; }
    }

    public bool FadeOut(int durationMs = 1000, Action? onComplete = null)
    {
        if (_disposed) return false;
        int h = _player.MixerHandle;
        if (h == 0) return false;

        try
        {
            bool ok = Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, 0f, Math.Max(1, durationMs));
            if (ok)
            {
                ScheduleFadeCompletion(pause: false, durationMs, onComplete);
            }
            return ok;
        }
        catch { return false; }
    }

    public bool FadeOutAndPause(int durationMs = 1000, Action? onComplete = null)
    {
        if (_disposed) return false;
        int h = _player.MixerHandle;
        if (h == 0) return false;

        try
        {
            bool ok = Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, 0f, Math.Max(1, durationMs));
            if (ok)
            {
                ScheduleFadeCompletion(pause: true, durationMs, onComplete);
            }
            return ok;
        }
        catch { return false; }
    }

    public SilenceAnalysisResult AnalyzeSilence(
        double thresholdDb = -50.0, int blockMs = 50, int minSilenceMs = 300)
        => SilenceAnalysisResult.NotAnalyzed;

    public NormalizationResult AnalyzeLevels() => NormalizationResult.NotAnalyzed;

    internal void ApplyBusVolume(double busVolume)
    {
        _busVolume = Math.Clamp(busVolume, 0.0, 1.0);
        ApplyCombinedVolume();
    }

    private bool ApplyCombinedVolume()
    {
        if (_disposed) return false;
        int h = _player.MixerHandle;
        if (h == 0) return false;

        try
        {
            double combined = ComputeCombinedVolume();
            return Bass.ChannelSetAttribute(h, ChannelAttribute.Volume, (float)combined);
        }
        catch { return false; }
    }

    private double ComputeCombinedVolume()
    {
        double normFactor = Math.Pow(10.0, _normGainDb / 20.0);
        double duckFactor = Math.Pow(10.0, _duckingGainDb / 20.0);
        double combined = _userVolume * _busVolume * normFactor * duckFactor * _globalVolume;
        if (combined > 4.0) combined = 4.0;
        if (combined < 0.0) combined = 0.0;
        return combined;
    }

    private void StartTrimEndTimer()
    {
        StopTrimEndTimer();
        if (_trimEnd <= TimeSpan.Zero) return;

        var effectiveEnd = Duration - _trimEnd;
        if (effectiveEnd <= TimeSpan.Zero) return;

        _trimEndTimer = new Timer(_ =>
        {
            if (_disposed) return;
            try
            {
                if (Position >= effectiveEnd)
                {
                    try { _player.Stop(); } catch { }
                    NotifyEnded(wasStoppedByUser: false);
                    StopTrimEndTimer();
                }
            }
            catch { }
        }, null, 15, 15);
    }

    private void ScheduleFadeCompletion(bool pause, int durationMs, Action? onComplete)
    {
        lock (_fadeGate)
        {
            CancelPendingFadeCompletionsLocked();

            Timer? timer = null;
            timer = new Timer(_ =>
            {
                lock (_fadeGate)
                {
                    var current = pause ? _fadeOutPauseCompletionTimer : _fadeOutCompletionTimer;
                    if (!ReferenceEquals(current, timer) || _disposed) return;

                    if (pause) _fadeOutPauseCompletionTimer = null;
                    else _fadeOutCompletionTimer = null;
                    timer?.Dispose();

                    try
                    {
                        if (pause) _player.Pause();
                        else _player.Stop();

                        ApplyCombinedVolume();
                        RaiseState(pause ? PlaybackState.Paused : PlaybackState.Stopped);
                        try { onComplete?.Invoke(); } catch { }
                    }
                    catch { }
                }
            }, null, Timeout.Infinite, Timeout.Infinite);

            if (pause) _fadeOutPauseCompletionTimer = timer;
            else _fadeOutCompletionTimer = timer;
            timer.Change(Math.Max(1, durationMs), Timeout.Infinite);
        }
    }

    private void CancelPendingFadeCompletions()
    {
        lock (_fadeGate)
            CancelPendingFadeCompletionsLocked();
    }

    private void CancelPendingFadeCompletionsLocked()
    {
        try { _fadeOutCompletionTimer?.Dispose(); } catch { }
        try { _fadeOutPauseCompletionTimer?.Dispose(); } catch { }
        _fadeOutCompletionTimer = null;
        _fadeOutPauseCompletionTimer = null;
    }

    private void StopTrimEndTimer()
    {
        try { _trimEndTimer?.Dispose(); } catch { }
        _trimEndTimer = null;
    }

    private void StartAutoFadeOutTimer()
    {
        StopAutoFadeOutTimer();
        if (!_autoFadeEnabled || _autoFadeSeconds <= 0.05) return;

        double fadeEnd = _trimEnd > TimeSpan.Zero
            ? (Duration - _trimEnd).TotalSeconds
            : Duration.TotalSeconds;

        double fadeStart = fadeEnd - _autoFadeSeconds;
        if (fadeStart <= 0) return;

        double delayMs = (fadeStart - Position.TotalSeconds) * 1000.0;
        if (delayMs <= 0) return;

        _autoFadeOutTimer = new Timer(_ =>
        {
            if (_disposed) return;
            try
            {
                int h = _player.MixerHandle;
                if (h == 0) return;
                int ms = (int)(_autoFadeSeconds * 1000);
                Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, 0f, ms);
            }
            catch { }
        }, null, (long)delayMs, Timeout.Infinite);
    }

    private void StopAutoFadeOutTimer()
    {
        try { _autoFadeOutTimer?.Dispose(); } catch { }
        _autoFadeOutTimer = null;
    }

    public void SetCrossfade(double crossfadeSeconds)
    {
        if (_disposed) return;
        _crossfadeSeconds = Math.Max(0, crossfadeSeconds);
        StartNearEndTimer();
    }

    private void StartNearEndTimer()
    {
        StopNearEndTimer();

        if (_disposed) return;
        if (_crossfadeSeconds <= 0.05) return;

        var dur = Duration;
        if (dur <= TimeSpan.Zero) return;

        double nearEndSec = (dur - _trimEnd).TotalSeconds - _crossfadeSeconds;
        if (nearEndSec <= 0.1) return;

        _nearEndTimer = new Timer(_ =>
        {
            if (_disposed) return;
            try
            {
                if (Position >= TimeSpan.FromSeconds(nearEndSec))
                {
                    StopNearEndTimer();
                    try { NearEnd?.Invoke(this, EventArgs.Empty); } catch { }
                }
            }
            catch { }
        }, null, 15, 15);
    }

    private void StopNearEndTimer()
    {
        try { _nearEndTimer?.Dispose(); } catch { }
        _nearEndTimer = null;
    }

    private void OnPlaybackEnded()
    {
        StopTrimEndTimer();
        StopAutoFadeOutTimer();
        try
        {
            bool wasStoppedByUser = _userStopped;
            _userStopped = false;
            NotifyEnded(wasStoppedByUser);
        }
        catch { }
    }

    private void NotifyEnded(bool wasStoppedByUser)
    {
        if (Interlocked.Exchange(ref _endEventRaised, 1) != 0) return;
        RaiseState(PlaybackState.Ended);
        try { Ended?.Invoke(this, new ChannelEndedEventArgs { WasStoppedByUser = wasStoppedByUser }); }
        catch { }
    }

    private void RaiseState(PlaybackState state)
    {
        try { StateChanged?.Invoke(this, state); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelPendingFadeCompletions();
        StopTrimEndTimer();
        StopAutoFadeOutTimer();
        StopNearEndTimer();

        try { _player.PlaybackEnded -= OnPlaybackEnded; } catch { }
        try { _player.Dispose(); } catch { }
        try { _equalizer.Dispose(); } catch { }
        try { _onDisposed?.Invoke(this); } catch { }
    }
}
