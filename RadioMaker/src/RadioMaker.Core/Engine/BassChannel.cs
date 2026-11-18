using ManagedBass;
using RadioMaker.Core.Abstractions;
using PlaybackState = RadioMaker.Core.Abstractions.PlaybackState;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Implementacion de <see cref="IAudioChannel"/> sobre un handle de BASS.
///
/// Cadena de volumen efectivo:
///     output = userVolume * busVolume * 10^(normGainDb/20) * 10^(duckingGainDb/20)
///
/// Incluye:
///   - Meter (VU)
///   - Equalizer (EQ 10 bandas)
///   - Analisis de silencios y niveles
///   - Auto-trim (recorte de silencio inicial/final)
///   - Normalizacion (ganancia adicional en dB)
///   - Auto-fade (fade-in al arrancar + fade-out antes del final)
///   - Ducking gain (atenuacion por microfono activo)
/// </summary>
internal sealed class BassChannel : IAudioChannel
{
    private readonly SyncProcedure _endSync;
    private readonly SyncProcedure _fadeOutSlideSync;
    private readonly SyncProcedure _fadeOutPauseSync;
    private readonly SyncProcedure _endTrimSync;
    private readonly SyncProcedure _autoFadeOutTriggerSync;
    private readonly SyncProcedure _nearEndSync;

    private readonly int _endSyncHandle;
    private int _fadeOutSyncHandle;
    private int _fadeOutPauseSyncHandle;
    private int _endTrimSyncHandle;
    private int _autoFadeOutTriggerSyncHandle;
    private int _nearEndSyncHandle;
    private double _crossfadeSeconds;

    private Action? _fadeOutCallback;

    private bool _disposed;
    private volatile bool _userStopped;
    private int _endEventRaised;
    private volatile bool _endTrimReached;
    private volatile bool _autoFadeOutActive;

    private double _userVolume = 1.0;
    private double _busVolume;
    private double _normGainDb = 0.0;
    private double _duckingGainDb = 0.0;
    private double _globalVolume = 1.0;

    private TimeSpan _trimStart = TimeSpan.Zero;
    private TimeSpan _trimEnd = TimeSpan.Zero;

    private bool _autoFadeEnabled;
    private double _autoFadeSeconds;

    private BassLevelMeter? _meter;
    private BassEqualizer? _equalizer;
    private BassSpectrum? _spectrum;
    private readonly Action<BassChannel>? _onDisposed;

    public event EventHandler<ChannelEndedEventArgs>? Ended;
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler? NearEnd;

    public int Handle { get; }
    public string? FilePath { get; }
    public string BusId { get; }

    public IAudioLevelMeter Meter => _meter ??= new BassLevelMeter(Handle);
    public IEqualizer Equalizer => _equalizer ??= new BassEqualizer(Handle);
    public IAudioSpectrum Spectrum => _spectrum ??= new BassSpectrum(Handle);

    public TimeSpan TrimStart => _trimStart;
    public TimeSpan TrimEnd => _trimEnd;
    public double NormalizationGainDb => _normGainDb;
    public double DuckingGainDb => _duckingGainDb;
    public bool IsAutoFadeEnabled => _autoFadeEnabled;
    public double AutoFadeSeconds => _autoFadeSeconds;

    public TimeSpan EffectiveDuration
    {
        get
        {
            var total = Duration;
            var effective = total - _trimStart - _trimEnd;
            return effective > TimeSpan.Zero ? effective : TimeSpan.Zero;
        }
    }

    public PlaybackState State
    {
        get
        {
            if (_disposed) return PlaybackState.Stopped;
            return MapState(Bass.ChannelIsActive(Handle));
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_disposed) return TimeSpan.Zero;
            long len = Bass.ChannelGetLength(Handle, PositionFlags.Bytes);
            if (len <= 0) return TimeSpan.Zero;
            double seconds = Bass.ChannelBytes2Seconds(Handle, len);
            return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
        }
    }

    public TimeSpan Position
    {
        get
        {
            if (_disposed) return TimeSpan.Zero;
            long pos = Bass.ChannelGetPosition(Handle, PositionFlags.Bytes);
            if (pos <= 0) return TimeSpan.Zero;
            double seconds = Bass.ChannelBytes2Seconds(Handle, pos);
            return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
        }
    }

    internal BassChannel(
        int handle,
        string filePath,
        string busId,
        double busVolume,
        Action<BassChannel>? onDisposed = null)
    {
        Handle = handle;
        FilePath = filePath;
        BusId = busId;
        _busVolume = busVolume;
        _onDisposed = onDisposed;
        _endSync = OnEndSync;
        _fadeOutSlideSync = OnFadeOutSlideCompleted;
        _fadeOutPauseSync = OnFadeOutPauseCompleted;
        _endTrimSync = OnEndTrimReached;
        _autoFadeOutTriggerSync = OnAutoFadeOutTrigger;
        _nearEndSync = OnNearEndReached;

        _endSyncHandle = Bass.ChannelSetSync(
            Handle, SyncFlags.End, 0L, _endSync, IntPtr.Zero);

        ApplyCombinedVolume();
    }

    // ------------------------------------------------------------ Reproduccion

    public bool Play()
    {
        if (_disposed) return false;
        Interlocked.Exchange(ref _endEventRaised, 0);
        _userStopped = false;
        _endTrimReached = false;
        _autoFadeOutActive = false;

        // ===== FIX trimEnd =====
        // Si hay trimEnd configurado pero el sync no se registro (porque
        // Duration era 0 al llamar SetTrim), intentar registrarlo ahora
        // que BASS ya tiene metadata cargada.
        if (_trimEnd > TimeSpan.Zero && _endTrimSyncHandle == 0)
        {
            RegisterEndTrimSync();
        }

        // Si hay autoFade y no habia trigger registrado por el mismo motivo
        // (Duration=0), registrarlo ahora.
        if (_autoFadeEnabled && _autoFadeSeconds > 0.05 && _autoFadeOutTriggerSyncHandle == 0)
        {
            RegisterAutoFadeOut();
        }

        var current = Bass.ChannelIsActive(Handle);

        if (current != ManagedBass.PlaybackState.Paused)
        {
            if (_trimStart > TimeSpan.Zero)
            {
                long trimStartBytes = Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds);
                if (trimStartBytes > 0)
                    Bass.ChannelSetPosition(Handle, trimStartBytes, PositionFlags.Bytes);
            }

            if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
            {
                Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, 0f);
            }
            else
            {
                ApplyCombinedVolume();
            }
        }

        bool ok = Bass.ChannelPlay(Handle, false);
        if (!ok) return false;

        if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
        {
            StartAutoFadeIn();
            RegisterAutoFadeOut();
        }

        RaiseState(PlaybackState.Playing);
        return true;
    }

    public bool Pause()
    {
        if (_disposed) return false;
        bool ok = Bass.ChannelPause(Handle);
        if (ok) RaiseState(PlaybackState.Paused);
        return ok;
    }

    public bool Stop()
    {
        if (_disposed) return false;
        bool wasActive = State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stalled;
        _userStopped = true;
        _endTrimReached = false;
        _autoFadeOutActive = false;

        UnregisterNearEndSync();

        bool ok = Bass.ChannelStop(Handle);
        if (ok)
        {
            UnregisterAutoFadeOut();

            long targetBytes = _trimStart > TimeSpan.Zero
                ? Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds)
                : 0L;

            try { Bass.ChannelSetPosition(Handle, targetBytes, PositionFlags.Bytes); }
            catch { }

            ApplyCombinedVolume();
            RaiseState(PlaybackState.Stopped);
            if (wasActive) NotifyEnded(wasStoppedByUser: true);
        }
        return ok;
    }

    // ------------------------------------------------------------ Auto-fade

    public void SetAutoFade(bool enabled, double fadeSeconds)
    {
        if (_disposed) return;

        _autoFadeEnabled = enabled;
        _autoFadeSeconds = Math.Clamp(fadeSeconds, 0.0, 30.0);

        var state = Bass.ChannelIsActive(Handle);
        if (state == ManagedBass.PlaybackState.Playing)
        {
            if (_autoFadeEnabled && _autoFadeSeconds > 0.05)
                RegisterAutoFadeOut();
            else
                UnregisterAutoFadeOut();
        }
    }

    private void StartAutoFadeIn()
    {
        int ms = (int)Math.Round(_autoFadeSeconds * 1000.0);
        if (ms <= 0) return;

        float target = (float)ComputeCombinedVolume();

        Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, 0f);
        Bass.ChannelSlideAttribute(Handle, ChannelAttribute.Volume, target, ms);
    }

    private void RegisterAutoFadeOut()
    {
        UnregisterAutoFadeOut();

        if (!_autoFadeEnabled || _autoFadeSeconds <= 0.05) return;

        double endUseful = (Duration - _trimEnd).TotalSeconds;
        if (endUseful <= 0) return;

        double fadeOutStart = endUseful - _autoFadeSeconds;
        if (fadeOutStart <= 0)
        {
            fadeOutStart = 0.01;
        }

        long triggerBytes = Bass.ChannelSeconds2Bytes(Handle, fadeOutStart);
        if (triggerBytes <= 0) return;

        _autoFadeOutTriggerSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Position,
            triggerBytes,
            _autoFadeOutTriggerSync,
            IntPtr.Zero);
    }

    private void UnregisterAutoFadeOut()
    {
        if (_autoFadeOutTriggerSyncHandle == 0) return;

        try { Bass.ChannelRemoveSync(Handle, _autoFadeOutTriggerSyncHandle); }
        catch { }

        _autoFadeOutTriggerSyncHandle = 0;
    }

    private void OnAutoFadeOutTrigger(int handle, int channel, int data, IntPtr user)
    {
        if (_disposed || _autoFadeOutActive) return;
        _autoFadeOutActive = true;

        int ms = (int)Math.Round(_autoFadeSeconds * 1000.0);
        if (ms <= 0) return;

        _fadeOutCallback = () =>
        {
            if (!_endTrimReached)
                NotifyEnded(wasStoppedByUser: false);
        };

        if (_fadeOutSyncHandle != 0)
        {
            Bass.ChannelRemoveSync(Handle, _fadeOutSyncHandle);
            _fadeOutSyncHandle = 0;
        }

        _fadeOutSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Slided,
            (long)ChannelAttribute.Volume,
            _fadeOutSlideSync,
            IntPtr.Zero);

        Bass.ChannelSlideAttribute(Handle, ChannelAttribute.Volume, 0f, ms);
    }

    // ------------------------------------------------------------ Auto-trim

    public void SetTrim(TimeSpan trimStart, TimeSpan trimEnd)
    {
        if (_disposed) return;

        if (trimStart < TimeSpan.Zero) trimStart = TimeSpan.Zero;
        if (trimEnd < TimeSpan.Zero) trimEnd = TimeSpan.Zero;

        var total = Duration;

        try
        {
            RadioMaker.Core.Logging.AppLogger.Debug("TRIM",
                $"SetTrim: file='{System.IO.Path.GetFileName(FilePath)}', " +
                $"Duration={total.TotalSeconds:F2}s, " +
                $"trimStart={trimStart.TotalSeconds:F2}s, " +
                $"trimEnd={trimEnd.TotalSeconds:F2}s");
        }
        catch { }

        // Si BASS todavía no conoce la duración, conservar el trim. Play()
        // vuelve a intentar registrar el recorte final cuando la metadata
        // ya está disponible.
        if (total > TimeSpan.Zero && trimStart + trimEnd >= total)
        {
            try
            {
                RadioMaker.Core.Logging.AppLogger.Warning("TRIM",
                    $"SetTrim cancelado: trimStart+trimEnd >= Duration " +
                    $"(Duration={total.TotalSeconds:F2}s). Llamando ClearTrim.");
            }
            catch { }
            ClearTrim();
            return;
        }

        _trimStart = trimStart;
        _trimEnd = trimEnd;

        RegisterEndTrimSync();

        if (_crossfadeSeconds > 0.05)
            RegisterNearEndSync();

        if (_autoFadeEnabled && _autoFadeSeconds > 0.05 &&
            Bass.ChannelIsActive(Handle) == ManagedBass.PlaybackState.Playing)
        {
            RegisterAutoFadeOut();
        }

        var state = Bass.ChannelIsActive(Handle);
        if (state == ManagedBass.PlaybackState.Playing || state == ManagedBass.PlaybackState.Paused)
        {
            // ===== REVERT: solo seek FORWARD, nunca BACKWARD =====
            // NOTA: si el analisis llega tarde (cache miss en memoria pero
            // .rmt en disco, leido async), Position ya esta en ~1s. Hacer
            // seek BACK a trimStart (0.5s) se percibe como restart.
            //
            // Solucion: solo seek si el audio TODAVIA no llego al trimStart.
            // Si ya lo paso (porque arranco desde 0 antes de que el analisis
            // llegara), dejarlo sonar sin tocar. Mejor un trimStart que no
            // se aplica en el primer play, que un restart visible.
            //
            // (El fix real es sincronizar la lectura del .rmt en
            // ChannelConfigurator antes del Play, ver ese archivo.)
            if (Position < _trimStart)
            {
                long bytes = Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds);
                if (bytes > 0)
                {
                    Bass.ChannelSetPosition(Handle, bytes, PositionFlags.Bytes);

                    try
                    {
                        RadioMaker.Core.Logging.AppLogger.Debug("TRIM",
                            $"Seek FORWARD al trimStart: '{System.IO.Path.GetFileName(FilePath)}' " +
                            $"pos {Position.TotalSeconds:F2}s -> {_trimStart.TotalSeconds:F2}s");
                    }
                    catch { }
                }
            }
        }
    }

    public void ClearTrim()
    {
        if (_disposed) return;

        _trimStart = TimeSpan.Zero;
        _trimEnd = TimeSpan.Zero;

        UnregisterEndTrimSync();

        if (_autoFadeEnabled && _autoFadeSeconds > 0.05 &&
            Bass.ChannelIsActive(Handle) == ManagedBass.PlaybackState.Playing)
        {
            RegisterAutoFadeOut();
        }

        if (_crossfadeSeconds > 0.05) RegisterNearEndSync();
    }

    private void RegisterEndTrimSync()
    {
        UnregisterEndTrimSync();

        if (_trimEnd <= TimeSpan.Zero) return;

        var dur = Duration;
        double endSeconds = (dur - _trimEnd).TotalSeconds;

        try
        {
            RadioMaker.Core.Logging.AppLogger.Debug("TRIM-END",
                $"RegisterEndTrimSync: file='{System.IO.Path.GetFileName(FilePath)}', " +
                $"Duration={dur.TotalSeconds:F2}s, trimEnd={_trimEnd.TotalSeconds:F2}s, " +
                $"endSeconds={endSeconds:F2}s");
        }
        catch { }

        if (endSeconds <= 0)
        {
            try
            {
                RadioMaker.Core.Logging.AppLogger.Warning("TRIM-END",
                    $"trimEnd NO registrado: endSeconds={endSeconds:F2}s <= 0 " +
                    $"(Duration={dur.TotalSeconds:F2}s, trimEnd={_trimEnd.TotalSeconds:F2}s). " +
                    $"Esto ocurre si BASS no conoce la duracion real del archivo.");
            }
            catch { }
            return;
        }

        long endBytes = Bass.ChannelSeconds2Bytes(Handle, endSeconds);
        if (endBytes <= 0) return;

        _endTrimSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Position,
            endBytes,
            _endTrimSync,
            IntPtr.Zero);

        try
        {
            RadioMaker.Core.Logging.AppLogger.Debug("TRIM-END",
                $"Sync registrado: handle={_endTrimSyncHandle}, " +
                $"endBytes={endBytes}, endSeconds={endSeconds:F2}s");
        }
        catch { }
    }

    private void UnregisterEndTrimSync()
    {
        if (_endTrimSyncHandle == 0) return;

        try { Bass.ChannelRemoveSync(Handle, _endTrimSyncHandle); }
        catch { }

        _endTrimSyncHandle = 0;
    }

    private void OnEndTrimReached(int handle, int channel, int data, IntPtr user)
    {
        if (_endTrimReached) return;
        if (_autoFadeOutActive) return;

        _endTrimReached = true;
        _userStopped = false;

        try
        {
            Bass.ChannelStop(Handle);

            long bytes = _trimStart > TimeSpan.Zero
                ? Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds)
                : 0L;
            Bass.ChannelSetPosition(Handle, bytes, PositionFlags.Bytes);

            ApplyCombinedVolume();
        }
        catch { }

        NotifyEnded(wasStoppedByUser: false);
    }

    public void SetCrossfade(double crossfadeSeconds)
    {
        if (_disposed) return;
        _crossfadeSeconds = Math.Max(0, crossfadeSeconds);
        RegisterNearEndSync();
    }

    private void RegisterNearEndSync()
    {
        UnregisterNearEndSync();

        if (_disposed) return;
        if (_crossfadeSeconds <= 0.05) return;

        var dur = Duration;
        if (dur <= TimeSpan.Zero) return;

        double nearEndSec = (dur - _trimEnd).TotalSeconds - _crossfadeSeconds;
        if (nearEndSec <= 0.1) return;

        long nearEndBytes = Bass.ChannelSeconds2Bytes(Handle, nearEndSec);
        if (nearEndBytes <= 0) return;

        _nearEndSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Position | SyncFlags.Onetime,
            nearEndBytes,
            _nearEndSync,
            IntPtr.Zero);

        try
        {
            RadioMaker.Core.Logging.AppLogger.Debug("CROSSFADE",
                $"NearEnd sync registrado en '{System.IO.Path.GetFileName(FilePath)}' " +
                $"@ {nearEndSec:F2}s (Duration={dur.TotalSeconds:F2}s, trimEnd={_trimEnd.TotalSeconds:F2}s, fade={_crossfadeSeconds:F2}s)");
        }
        catch { }
    }

    private void UnregisterNearEndSync()
    {
        if (_nearEndSyncHandle == 0) return;
        try { Bass.ChannelRemoveSync(Handle, _nearEndSyncHandle); } catch { }
        _nearEndSyncHandle = 0;
    }

    private void OnNearEndReached(int handle, int channel, int data, IntPtr user)
    {
        _nearEndSyncHandle = 0; // Onetime
        try { NearEnd?.Invoke(this, EventArgs.Empty); } catch { }
    }

    // ------------------------------------------------------------ Normalizacion

    public void SetNormalizationGain(double gainDb)
    {
        if (_disposed) return;
        _normGainDb = Math.Clamp(gainDb, -60.0, 60.0);
        ApplyCombinedVolume();
    }

    public void ClearNormalization()
    {
        if (_disposed) return;
        _normGainDb = 0.0;
        ApplyCombinedVolume();
    }

    // ------------------------------------------------------------ Ducking

    public void SetDuckingGain(double gainDb)
    {
        if (_disposed) return;

        // Limitamos a -60 dB (practicamente mute) por seguridad.
        double clamped = Math.Clamp(gainDb, -60.0, 0.0);

        // Solo aplicar si cambio significativamente (evita saturar BASS con
        // llamadas innecesarias a 50 Hz).
        if (Math.Abs(clamped - _duckingGainDb) < 0.05) return;

        _duckingGainDb = clamped;
        ApplyCombinedVolume();
    }

    public void SetGlobalVolume(double volume)
    {
        if (_disposed) return;
        double clamped = Math.Clamp(volume, 0.0, 1.0);
        if (Math.Abs(clamped - _globalVolume) < 0.001) return;

        _globalVolume = clamped;
        ApplyCombinedVolume();
    }

    // ------------------------------------------------------------ Volumen

    public bool SetVolume(double volume)
    {
        if (_disposed) return false;
        _userVolume = Math.Clamp(volume, 0.0, 1.0);
        return ApplyCombinedVolume();
    }

    public bool Seek(TimeSpan position)
    {
        if (_disposed) return false;

        double seconds = position.TotalSeconds;
        if (seconds < 0) seconds = 0;

        long bytes = Bass.ChannelSeconds2Bytes(Handle, seconds);
        if (bytes <= 0) return false;

        return Bass.ChannelSetPosition(Handle, bytes, PositionFlags.Bytes);
    }

    internal void ApplyBusVolume(double busVolume)
    {
        _busVolume = Math.Clamp(busVolume, 0.0, 1.0);
        ApplyCombinedVolume();
    }

    private bool ApplyCombinedVolume()
    {
        if (_disposed) return false;

        double combined = ComputeCombinedVolume();
        return Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, (float)combined);
    }

    /// <summary>
    /// Calcula el volumen combinado final:
    ///     userVolume * busVolume * 10^(normGainDb/20) * 10^(duckingGainDb/20)
    /// Limitado a 4.0 (aprox +12 dB) para evitar saturaciones catastroficas.
    /// </summary>
    private double ComputeCombinedVolume()
    {
        double normFactor = Math.Pow(10.0, _normGainDb / 20.0);
        double duckFactor = Math.Pow(10.0, _duckingGainDb / 20.0);

        double combined = _userVolume * _busVolume * normFactor * duckFactor * _globalVolume;

        if (combined > 4.0) combined = 4.0;
        if (combined < 0.0) combined = 0.0;
        return combined;
    }

    // ------------------------------------------------------------ FadeIn / FadeOut manuales

    public bool FadeIn(int durationMs = 1000, double targetVolume = 1.0, bool preservePosition = false)
    {
        if (_disposed) return false;

        // ===== FIX CRÍTICO: remover syncs pendientes de FadeOut/FadeOutAndPause =====
        // Si el canal viene de un FadeOutAndPause (expropiado por el scheduler),
        // el _fadeOutPauseSyncHandle sigue activo. Cuando el fade-in actual
        // termine, BASS dispararia ese sync Slided y el canal se pausaria de
        // nuevo (bug: "se reanuda 2-3 segundos y se detiene"). Lo mismo aplica
        // para _fadeOutSyncHandle si viene de un FadeOut que no completo.
        try
        {
            if (_fadeOutPauseSyncHandle != 0)
            {
                Bass.ChannelRemoveSync(Handle, _fadeOutPauseSyncHandle);
                _fadeOutPauseSyncHandle = 0;
            }
            if (_fadeOutSyncHandle != 0)
            {
                Bass.ChannelRemoveSync(Handle, _fadeOutSyncHandle);
                _fadeOutSyncHandle = 0;
            }
        }
        catch { }

        _fadeOutCallback = null;

        _userVolume = Math.Clamp(targetVolume, 0.0, 1.0);
        float target = (float)ComputeCombinedVolume();
        bool needsStart = Bass.ChannelIsActive(Handle) != ManagedBass.PlaybackState.Playing;
        if (needsStart)
            Interlocked.Exchange(ref _endEventRaised, 0);

        Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, 0f);

        bool ok = Bass.ChannelSlideAttribute(
            Handle, ChannelAttribute.Volume, target, Math.Max(1, durationMs));

        if (ok && needsStart)
        {
            if (!preservePosition && _trimStart > TimeSpan.Zero)
            {
                long bytes = Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds);
                if (bytes > 0)
                    Bass.ChannelSetPosition(Handle, bytes, PositionFlags.Bytes);
            }

            ok = Bass.ChannelPlay(Handle, false) && ok;

            if (ok && _autoFadeEnabled && _autoFadeSeconds > 0.05)
                RegisterAutoFadeOut();
        }

        if (ok) RaiseState(PlaybackState.Playing);
        return ok;
    }

    public bool FadeOut(int durationMs = 1000, Action? onComplete = null)
    {
        if (_disposed) return false;

        _fadeOutCallback = onComplete;

        if (_fadeOutSyncHandle != 0)
        {
            Bass.ChannelRemoveSync(Handle, _fadeOutSyncHandle);
            _fadeOutSyncHandle = 0;
        }

        _fadeOutSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Slided | SyncFlags.Onetime,
            (long)ChannelAttribute.Volume,
            _fadeOutSlideSync,
            IntPtr.Zero);

        return Bass.ChannelSlideAttribute(
            Handle, ChannelAttribute.Volume, 0f, Math.Max(1, durationMs));
    }

    private void OnFadeOutSlideCompleted(int handle, int channel, int data, IntPtr user)
    {
        // El sync es Onetime: BASS ya lo libero. Marcar handle 0 primero.
        _fadeOutSyncHandle = 0;

        try
        {
            Bass.ChannelStop(Handle);

            long bytes = _trimStart > TimeSpan.Zero
                ? Bass.ChannelSeconds2Bytes(Handle, _trimStart.TotalSeconds)
                : 0L;
            Bass.ChannelSetPosition(Handle, bytes, PositionFlags.Bytes);

            ApplyCombinedVolume();
        }
        catch { }

        var cb = _fadeOutCallback;
        _fadeOutCallback = null;

        RaiseState(PlaybackState.Stopped);
        try { cb?.Invoke(); } catch { }
    }

    public bool FadeOutAndPause(int durationMs = 1000, Action? onComplete = null)
    {
        if (_disposed) return false;

        _fadeOutCallback = onComplete;

        if (_fadeOutPauseSyncHandle != 0)
        {
            try { Bass.ChannelRemoveSync(Handle, _fadeOutPauseSyncHandle); } catch { }
            _fadeOutPauseSyncHandle = 0;
        }

        _fadeOutPauseSyncHandle = Bass.ChannelSetSync(
            Handle,
            SyncFlags.Slided | SyncFlags.Onetime,
            (long)ChannelAttribute.Volume,
            _fadeOutPauseSync,
            IntPtr.Zero);

        return Bass.ChannelSlideAttribute(
            Handle, ChannelAttribute.Volume, 0f, Math.Max(1, durationMs));
    }

    private void OnFadeOutPauseCompleted(int handle, int channel, int data, IntPtr user)
    {
        // El sync es Onetime: BASS ya lo libero. Marcar handle 0 primero.
        _fadeOutPauseSyncHandle = 0;

        try
        {
            Bass.ChannelPause(Handle);
        }
        catch { }

        var cb = _fadeOutCallback;
        _fadeOutCallback = null;

        RaiseState(PlaybackState.Paused);
        try { cb?.Invoke(); } catch { }
    }

    // ------------------------------------------------------------ Analisis

    public SilenceAnalysisResult AnalyzeSilence(
        double thresholdDb = -50.0,
        int blockMs = 50,
        int minSilenceMs = 300)
    {
        if (_disposed) return SilenceAnalysisResult.NotAnalyzed;
        if (string.IsNullOrEmpty(FilePath)) return SilenceAnalysisResult.NotAnalyzed;

        return BassSilenceAnalyzer.Analyze(FilePath, thresholdDb, blockMs, minSilenceMs);
    }

    public NormalizationResult AnalyzeLevels()
    {
        if (_disposed) return NormalizationResult.NotAnalyzed;
        if (string.IsNullOrEmpty(FilePath)) return NormalizationResult.NotAnalyzed;

        return BassLevelAnalyzer.Analyze(FilePath);
    }

    // ------------------------------------------------------------ Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_endSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _endSyncHandle);
            if (_fadeOutSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _fadeOutSyncHandle);
            if (_fadeOutPauseSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _fadeOutPauseSyncHandle);
            if (_endTrimSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _endTrimSyncHandle);
            if (_autoFadeOutTriggerSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _autoFadeOutTriggerSyncHandle);
            if (_nearEndSyncHandle != 0)
                Bass.ChannelRemoveSync(Handle, _nearEndSyncHandle);
        }
        catch { }

        try { _equalizer?.Dispose(); } catch { }
        try { Bass.StreamFree(Handle); } catch { }
        try { _onDisposed?.Invoke(this); } catch { }
    }

    // ------------------------------------------------------------ Helpers

    private void OnEndSync(int handle, int channel, int data, IntPtr user)
    {
        if (_endTrimReached) return;
        if (_autoFadeOutActive) return;

        bool userStopped = _userStopped;
        _userStopped = false;

        NotifyEnded(userStopped);
    }

    private void NotifyEnded(bool wasStoppedByUser)
    {
        if (Interlocked.Exchange(ref _endEventRaised, 1) != 0) return;
        try { RaiseState(PlaybackState.Ended); } catch { }
        try { Ended?.Invoke(this, new ChannelEndedEventArgs { WasStoppedByUser = wasStoppedByUser }); }
        catch { }
    }

    private void RaiseState(PlaybackState state) => StateChanged?.Invoke(this, state);

    private static PlaybackState MapState(ManagedBass.PlaybackState s) => s switch
    {
        ManagedBass.PlaybackState.Playing => PlaybackState.Playing,
        ManagedBass.PlaybackState.Paused  => PlaybackState.Paused,
        ManagedBass.PlaybackState.Stalled => PlaybackState.Stalled,
        ManagedBass.PlaybackState.Stopped => PlaybackState.Stopped,
        _ => PlaybackState.Stopped
    };
}
