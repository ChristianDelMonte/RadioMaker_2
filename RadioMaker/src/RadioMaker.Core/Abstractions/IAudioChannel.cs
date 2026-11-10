namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Representa un archivo de audio cargado en memoria, reproducible de forma independiente.
/// </summary>
public interface IAudioChannel : IDisposable
{
    event EventHandler<ChannelEndedEventArgs>? Ended;
    event EventHandler<PlaybackState>? StateChanged;

    /// <summary>
    /// Se dispara cuando el canal esta a `crossfadeSeconds` del fin efectivo
    /// (Duration - trimEnd - crossfadeSeconds). El host lo usa para iniciar
    /// el crossfade con el siguiente canal ANTES de que termine este.
    /// NO se dispara si el crossfade no esta configurado (crossfadeSeconds <= 0).
    /// </summary>
    event EventHandler? NearEnd;

    string BusId { get; }
    int Handle { get; }
    string? FilePath { get; }
    PlaybackState State { get; }
    TimeSpan Duration { get; }
    TimeSpan Position { get; }
    /// <summary>Medidor de niveles asociado al canal.</summary>
    IAudioLevelMeter Meter { get; }

    /// <summary>Ecualizador grafico asociado al canal.</summary>
    IEqualizer Equalizer { get; }

    /// <summary>
    /// Analizador espectral (FFT) y de forma de onda del canal.
    /// Se usa para alimentar el spectrum analyzer de la UI.
    /// </summary>
    IAudioSpectrum Spectrum { get; }

    TimeSpan TrimStart { get; }
    TimeSpan TrimEnd { get; }
    TimeSpan EffectiveDuration { get; }
    double NormalizationGainDb { get; }

    bool IsAutoFadeEnabled { get; }
    double AutoFadeSeconds { get; }

    /// <summary>
    /// Ganancia de ducking aplicada actualmente (dB, siempre &lt;= 0).
    /// 0 dB = sin ducking. Valores negativos indican atenuacion activa.
    /// </summary>
    double DuckingGainDb { get; }

    bool Play();
    bool Pause();
    bool Stop();
    bool SetVolume(double volume);

    /// <summary>
    /// Mueve la posicion del canal al instante indicado. Util para reanudar
    /// una reproduccion pausada en un punto especifico.
    /// </summary>
    bool Seek(TimeSpan position);

    void SetTrim(TimeSpan trimStart, TimeSpan trimEnd);
    void ClearTrim();

    /// <summary>
    /// Configura el aviso de near-end. Se debe llamar DESPUES de SetTrim
    /// (porque usa Duration y trimEnd para calcular el momento del aviso).
    /// crossfadeSeconds <= 0 desactiva el aviso.
    /// </summary>
    void SetCrossfade(double crossfadeSeconds);
    void SetNormalizationGain(double gainDb);
    void ClearNormalization();

    /// <summary>
    /// Aplica la ganancia de ducking (dB, valor negativo para atenuar).
    /// 0 dB = sin atenuacion.
    /// </summary>
    void SetDuckingGain(double gainDb);

    /// <summary>
    /// Aplica un factor de volumen global (0.0 .. 1.0) que se multiplica
    /// junto con el volumen del usuario, del bus, la normalizacion y el
    /// ducking. Sirve para el control de volumen "master" de la aplicacion.
    /// </summary>
    void SetGlobalVolume(double volume);

    void SetAutoFade(bool enabled, double fadeSeconds);

    /// <summary>
    /// Fade-in del canal. Si <paramref name="preservePosition"/> es <c>true</c>,
    /// NO se hace seek al trimStart (se usa la posicion actual del canal).
    /// Util para reanudar un canal pausado sin perder el punto de resume.
    /// </summary>
    bool FadeIn(int durationMs = 1000, double targetVolume = 1.0, bool preservePosition = false);

    bool FadeOut(int durationMs = 1000, Action? onComplete = null);

    /// <summary>
    /// Fade-out del canal y al terminar lo deja en <c>Paused</c> en vez de
    /// <c>Stopped</c>. A diferencia de <see cref="FadeOut"/>, esta variante
    /// preserva la posicion del canal para que un futuro <c>Play()</c> lo
    /// reanude desde el mismo punto (sin re-seek).
    /// </summary>
    bool FadeOutAndPause(int durationMs = 1000, Action? onComplete = null);

    SilenceAnalysisResult AnalyzeSilence(
        double thresholdDb = -50.0,
        int blockMs = 50,
        int minSilenceMs = 300);

    NormalizationResult AnalyzeLevels();
}