using System.Collections.Concurrent;
using System.Threading.Tasks;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Aplica automaticamente los ajustes de EQ, Trim y Normalizacion a cualquier
/// canal que este por reproducirse. Emite un log detallado de cada aplicacion.
///
/// Flujo de analisis (trim + norm):
///   1. In-memory cache hit → aplicar directo (rapidisimo).
///   2. Persistent cache hit (.rmt) → cargar, guardar en memoria, aplicar.
///   3. Cache miss total → analizar en background, guardar en memoria Y en
///      disco (.rmt), aplicar retroactivamente cuando termine.
///
/// DEDUPLICACION DE ANALISIS:
///   Si el usuario mueve sliders rapidamente antes de que termine el primer
///   analisis, NO se lanzan analisis paralelos. Se usa _pendingAnalysis para
///   garantizar un solo analisis en curso por archivo.
///
/// NO LOGGING EN REAPLICACIONES MASIVAS:
///   ReapplyToAll se dispara muchas veces por segundo (cada movimiento de
///   slider). NO loguea para evitar spam. Solo se loguean los ajustes reales
///   cuando logDetails=true (primera vez que se carga un canal).
/// -------------
public sealed class ChannelConfigurator
{
    private readonly EqualizerSettings _eqSettings;
    private readonly SilencePanelSettings _silenceSettings;
    private readonly NormalizationSettings _normSettings;

    private readonly ConcurrentDictionary<string, AnalysisCache> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _pendingAnalysis = new(StringComparer.OrdinalIgnoreCase);

    public Action<string>? AnalysisCompleted { get; set; }

    public bool IsAnalysisPending(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && _pendingAnalysis.ContainsKey(filePath);

    public ChannelConfigurator(
        EqualizerSettings eqSettings,
        SilencePanelSettings silenceSettings,
        NormalizationSettings normSettings)
    {
        _eqSettings = eqSettings ?? throw new ArgumentNullException(nameof(eqSettings));
        _silenceSettings = silenceSettings ?? throw new ArgumentNullException(nameof(silenceSettings));
        _normSettings = normSettings ?? throw new ArgumentNullException(nameof(normSettings));
    }

    // ───── API publica ───────────────────────────────────────────────────

    /// -------------
    /// Aplica todos los ajustes automaticos al canal, ANTES de que arranque
    /// a reproducir.
    /// -------------
    public void ConfigureChannel(
        IAudioChannel channel,
        string filePath,
        string sourceCategory = "CONFIG",
        bool logDetails = true)
    {
        if (channel is null) return;
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var fileName = Path.GetFileName(filePath);

        // 1) EQ: aplicacion inmediata.
        ApplyEqualizer(channel, sourceCategory, fileName, logDetails);

        // 2) Cache en memoria (rapidisimo, ya lo teniamos del mismo archivo).
        if (_cache.TryGetValue(filePath, out var cached))
        {
            ApplyTrim(channel, cached.Silence, sourceCategory, fileName, logDetails);
            ApplyNormalization(channel, cached.Levels, sourceCategory, fileName, logDetails);
            return;
        }

        // 3) ===== FIX restart: lectura SINCRONA del .rmt =====
        // Antes el .rmt se leia async en LoadOrAnalyzeInBackgroundAsync.
        // En cold-start (app recien abierta, .rmt en disco pero no en
        // memoria), el flujo era:
        //   Play → audio arranca → ~1s despues el async lee el .rmt →
        //   ApplyTrim → SetTrim con seek-back al trimStart → sensacion
        //   de "restart" del audio.
        //
        // Fix: leer el .rmt sincronamente en ConfigureChannel, ANTES del
        // Play. La lectura es rapida (~1-5ms, JSON de ~500 bytes) y NO
        // decodifica el audio. Es seguro en el hilo de carga del canal.
        try
        {
            var persisted = AudioAnalysisCacheStore.TryLoad(filePath);
            if (persisted is not null)
            {
                _cache[filePath] = new AnalysisCache
                {
                    Silence = persisted.Silence,
                    Levels = persisted.Levels
                };

                ApplyTrim(channel, persisted.Silence, sourceCategory, fileName, logDetails);
                ApplyNormalization(channel, persisted.Levels, sourceCategory, fileName, logDetails);

                try { AnalysisCompleted?.Invoke(filePath); } catch { }
                return;
            }
        }
        catch (Exception ex)
        {
            try
            {
                AppLogger.Warning(sourceCategory,
                    $"Error leyendo .rmt de {fileName}: {ex.Message}");
            }
            catch { }
        }

        // 4) Cache miss TOTAL (primera reproduccion en la vida del archivo):
        //    analizar en background. Este es el unico caso que sigue siendo
        //    async. Con el Prewarm de la proxima pista (ya implementado),
        //    solo afecta al primer track de la sesion.
        if (_pendingAnalysis.TryGetValue(filePath, out var pendingAnalysis))
        {
            // Un Prewarm puede estar analizando este archivo cuando empieza
            // la primera reproducción. No bloquear el Play ni abandonar el
            // canal: aplicar trim/norm al terminar el análisis.
            _ = pendingAnalysis.ContinueWith(
                _ =>
                {
                    if (!string.Equals(channel.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        return;
                    if (!_cache.TryGetValue(filePath, out var ready))
                        return;

                    ApplyTrim(channel, ready.Silence, sourceCategory, fileName, logDetails: false);
                    ApplyNormalization(channel, ready.Levels, sourceCategory, fileName, logDetails: false);
                    try { AnalysisCompleted?.Invoke(filePath); } catch { }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        var task = LoadOrAnalyzeInBackgroundAsync(channel, filePath, sourceCategory);
        _pendingAnalysis[filePath] = task;

        _ = task.ContinueWith(
            t => _pendingAnalysis.TryRemove(filePath, out _),
            TaskScheduler.Default);
    }

    /// -------------
    /// Reaplica la configuracion (EQ + Trim + Normalizacion) a una lista de
    /// canales activos, en modo silencioso (SIN LOG).
    ///
    /// IMPORTANTE: este metodo se dispara muchas veces por segundo cuando
    /// el usuario arrastra un slider del EQ. NO loguear aqui para evitar
    /// spam en el panel de log.
    /// -------------
    public void ReapplyToAll(IEnumerable<IAudioChannel?> channels)
    {
        if (channels is null) return;

        foreach (var ch in channels)
        {
            if (ch is null) continue;

            try
            {
                var path = ch.FilePath;
                if (string.IsNullOrEmpty(path)) continue;

                ConfigureChannel(ch, path, "CONFIG", logDetails: false);
            }
            catch { }
        }

        // NOTA: sin AppLogger aqui. Ver comentario del metodo.
    }

    /// -------------
    /// Pre-carga el analisis de un archivo en memoria sin tocar ningun canal.
    /// Util para pre-calentar la cache antes de que el usuario aprete Play.
    /// -------------
    public void Prewarm(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (_cache.ContainsKey(filePath)) return;
        if (_pendingAnalysis.ContainsKey(filePath)) return;

        var task = PrewarmInternalAsync(filePath);
        _pendingAnalysis[filePath] = task;

        _ = task.ContinueWith(
            t => _pendingAnalysis.TryRemove(filePath, out _),
            TaskScheduler.Default);
    }

    private async Task PrewarmInternalAsync(string filePath)
    {
        try
        {
            var persisted = await Task.Run(() => AudioAnalysisCacheStore.TryLoad(filePath));
            if (persisted is not null)
            {
                _cache[filePath] = new AnalysisCache
                {
                    Silence = persisted.Silence,
                    Levels = persisted.Levels
                };
                return;
            }

            var silence = await Task.Run(() => AudioAnalysis.AnalyzeSilence(
                filePath,
                _silenceSettings.ThresholdDb,
                _silenceSettings.BlockMs,
                _silenceSettings.MinSilenceMs));

            var levels = await Task.Run(() => AudioAnalysis.AnalyzeLevels(filePath));

            _cache[filePath] = new AnalysisCache
            {
                Silence = silence,
                Levels = levels
            };

            if (silence.IsAnalyzed || levels.IsAnalyzed)
            {
                try
                {
                    await Task.Run(() => AudioAnalysisCacheStore.Save(filePath, silence, levels));
                }
                catch { }
            }
        }
        catch { }
    }

    /// -------------
    /// Limpia solo el cache en memoria. Los .rmt persistentes se mantienen.
    /// -------------
    public void ClearCache() => _cache.Clear();

    // ───── Carga o analisis en background ───────────────────────────────

    private async Task LoadOrAnalyzeInBackgroundAsync(
        IAudioChannel channel,
        string filePath,
        string sourceCategory)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);

            // 1) Cache persistente: puede evitar decodificar por completo.
            var persisted = await Task.Run(() => AudioAnalysisCacheStore.TryLoad(filePath));
            if (persisted is not null)
            {
                _cache[filePath] = new AnalysisCache
                {
                    Silence = persisted.Silence,
                    Levels = persisted.Levels
                };

                ApplyTrim(channel, persisted.Silence, sourceCategory, fileName, logDetails: false);
                ApplyNormalization(channel, persisted.Levels, sourceCategory, fileName, logDetails: false);

                var trimInfo = persisted.Silence.IsAnalyzed
                    ? $"recorta {FormatMs(persisted.Silence.StartSilence)}/{FormatMs(persisted.Silence.EndSilence)}"
                    : "sin datos";
                var normInfo = persisted.Levels.IsAnalyzed
                    ? $"peak={persisted.Levels.PeakDb:F2}dB"
                    : "sin datos";

                AppLogger.Info(sourceCategory,
                    $"ANALISIS cacheado cargado para {fileName} | TRIM {trimInfo} | NORM {normInfo}");

                try { AnalysisCompleted?.Invoke(filePath); } catch { }
                return;
            }

            // 2) Analisis completo (primera vez o cache invalido).
            var silence = await Task.Run(() =>
                AudioAnalysis.AnalyzeSilence(
                    filePath,
                    _silenceSettings.ThresholdDb,
                    _silenceSettings.BlockMs,
                    _silenceSettings.MinSilenceMs));

            var levels = await Task.Run(() => AudioAnalysis.AnalyzeLevels(filePath));

            _cache[filePath] = new AnalysisCache
            {
                Silence = silence,
                Levels = levels
            };

            // Persistir a disco (solo si hay algo que guardar).
            if (silence.IsAnalyzed || levels.IsAnalyzed)
            {
                try
                {
                    await Task.Run(() => AudioAnalysisCacheStore.Save(filePath, silence, levels));
                }
                catch (Exception ex)
                {
                    AppLogger.Warning(sourceCategory,
                        $"No se pudo guardar el .rmt para {fileName}: {ex.Message}");
                }
            }

            ApplyTrim(channel, silence, sourceCategory, fileName, logDetails: false);
            ApplyNormalization(channel, levels, sourceCategory, fileName, logDetails: false);

            AppLogger.Info(sourceCategory,
                $"Analisis completado para {fileName}: " +
                $"TRIM {(silence.HasSignificantTrim ? $"recorta {FormatMs(silence.StartSilence)}/{FormatMs(silence.EndSilence)}" : "sin recortes")} | " +
                $"NORMALIZACION {(levels.IsAnalyzed ? $"ganancia calculada sobre peak={levels.PeakDb:F2}dB" : "sin datos")}");

            try { AnalysisCompleted?.Invoke(filePath); } catch { }
        }
        catch (Exception ex)
        {
            AppLogger.Error(sourceCategory, $"Error al analizar {Path.GetFileName(filePath)}", ex);
        }
    }

    // ───── Aplicacion de cada ajuste con log ─────────────────────────────

    private void ApplyEqualizer(IAudioChannel channel, string sourceCategory, string fileName, bool logDetails)
    {
        try
        {
            var eq = channel.Equalizer;

            eq.ApplyGains(_eqSettings.Gains);
            eq.SetEnabled(_eqSettings.IsEnabled);

            if (!logDetails) return;

            if (_eqSettings.IsEnabled)
            {
                var gainsDesc = string.Join(", ",
                    _eqSettings.Gains.Select((g, i) =>
                        $"{EqualizerSettings.FrequencyLabels[i]}={g:+0.0;-0.0;0.0}dB"));

                AppLogger.Info(sourceCategory,
                    $"EQ aplicado a {fileName} | {eq.BandCount} bandas | {gainsDesc}");
            }
            else
            {
                AppLogger.Info(sourceCategory, $"EQ desactivado para {fileName}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(sourceCategory, $"Error al aplicar EQ a {fileName}", ex);
        }
    }

    private void ApplyTrim(IAudioChannel channel, SilenceAnalysisResult silence, string sourceCategory, string fileName, bool logDetails)
    {
        try
        {
            if (!_silenceSettings.AutoTrimEnabled)
            {
                channel.ClearTrim();
                if (logDetails)
                    AppLogger.Info(sourceCategory, $"TRIM desactivado para {fileName}");
                return;
            }

            if (!silence.IsAnalyzed)
            {
                channel.ClearTrim();
                if (logDetails)
                    AppLogger.Info(sourceCategory, $"TRIM sin analisis para {fileName}");
                return;
            }

            if (!silence.HasSignificantTrim)
            {
                channel.ClearTrim();
                if (logDetails)
                    AppLogger.Info(sourceCategory,
                        $"TRIM activo sin recortes significativos para {fileName} | " +
                        $"Inicio: {FormatMs(silence.StartSilence)} | Fin: {FormatMs(silence.EndSilence)}");
                return;
            }

            channel.SetTrim(silence.StartSilence, silence.EndSilence);

            if (logDetails)
                AppLogger.Info(sourceCategory,
                    $"TRIM aplicado a {fileName} | " +
                    $"Inicio recortado: {FormatMs(silence.StartSilence)} | " +
                    $"Fin recortado: {FormatMs(silence.EndSilence)} | " +
                    $"Umbral: {silence.ThresholdDb:F1}dB");
        }
        catch (Exception ex)
        {
            AppLogger.Error(sourceCategory, $"Error al aplicar TRIM a {fileName}", ex);
        }
    }

    private void ApplyNormalization(IAudioChannel channel, NormalizationResult levels, string sourceCategory, string fileName, bool logDetails)
    {
        try
        {
            if (!_normSettings.IsEnabled)
            {
                channel.ClearNormalization();
                if (logDetails)
                    AppLogger.Info(sourceCategory, $"NORMALIZACION desactivada para {fileName}");
                return;
            }

            if (!levels.IsAnalyzed)
            {
                channel.ClearNormalization();
                if (logDetails)
                    AppLogger.Info(sourceCategory, $"NORMALIZACION sin analisis para {fileName}");
                return;
            }

            double gainDb = levels.ComputeGainDb(_normSettings.Mode, _normSettings.TargetDb);
            channel.SetNormalizationGain(gainDb);

            if (logDetails)
            {
                string mode = _normSettings.Mode == NormalizationMode.Peak ? "Peak" : "RMS";

                AppLogger.Info(sourceCategory,
                    $"NORMALIZACION aplicada a {fileName} | " +
                    $"Modo: {mode} | " +
                    $"Target: {_normSettings.TargetDb:F1}dBFS | " +
                    $"Peak original: {levels.PeakDb:F2}dB | " +
                    $"RMS original: {levels.RmsDb:F2}dB | " +
                    $"Ganancia aplicada: {gainDb:+0.00;-0.00;0.00}dB");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(sourceCategory, $"Error al aplicar NORMALIZACION a {fileName}", ex);
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private static string FormatMs(TimeSpan ts)
    {
        if (ts.TotalMilliseconds < 1) return "0ms";
        if (ts.TotalSeconds < 1) return $"{ts.TotalMilliseconds:F0}ms";
        return $"{ts:mm\\:ss\\.f}";
    }

    private sealed class AnalysisCache
    {
        public SilenceAnalysisResult Silence { get; init; } = SilenceAnalysisResult.NotAnalyzed;
        public NormalizationResult Levels { get; init; } = NormalizationResult.NotAnalyzed;
    }
}
