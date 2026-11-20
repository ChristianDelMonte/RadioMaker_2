using System.Collections.Concurrent;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Tags;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Progreso del generador. Se reporta al UI para mostrar mensajes de
/// estado mientras escanea la carpeta y lee las duraciones / tags.
/// </summary>
public sealed record PlaylistGeneratorProgress(
    string Message,
    int Current,
    int Total);

/// <summary>
/// Candidato de audio encontrado durante el escaneo de carpeta.
/// Lleva path, duracion y (opcionalmente) los tags del archivo.
///
/// Si el usuario NO activo filtros por tags ni reglas de variedad por
/// artista, Tags queda en Empty y no se leyo nunca.
/// </summary>
public sealed class AudioCandidate
{
    public string FilePath { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public AudioTagInfo Tags { get; init; } = AudioTagInfo.Empty;

    public string DisplayName =>
        string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>
    /// Artista normalizado. Vacio = "wildcard" para las reglas de variedad
    /// (nunca bloquea ni es bloqueado).
    /// </summary>
    public string Artist =>
        string.IsNullOrWhiteSpace(Tags?.Performers)
            ? string.Empty
            : Tags.Performers.Trim();

    public bool HasArtist => !string.IsNullOrEmpty(Artist);

    /// <summary>
    /// Texto secundario util para el preview.
    /// </summary>
    public string SubtitleText
    {
        get
        {
            if (Tags is null) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Tags.Performers)) parts.Add(Tags.Performers);
            if (!string.IsNullOrWhiteSpace(Tags.Album)) parts.Add(Tags.Album);

            if (parts.Count > 0) return string.Join(" — ", parts);

            return Tags.Genres ?? string.Empty;
        }
    }
}

/// <summary>
/// Resultado de una ejecucion del generador.
/// </summary>
public sealed record PlaylistGeneratorResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<AudioCandidate> SelectedTracks,
    int TotalCandidatesFound,
    int SkippedUnreadable,
    int FilteredOutByTags,
    int FilteredOutByDuration,
    int RelaxedByArtistRules)
{
    public double TotalDurationSeconds
    {
        get
        {
            double sum = 0;
            foreach (var c in SelectedTracks) sum += c.DurationSeconds;
            return sum;
        }
    }

    public int TrackCount => SelectedTracks.Count;

    public static PlaylistGeneratorResult Fail(string message) =>
        new(false, message, Array.Empty<AudioCandidate>(), 0, 0, 0, 0, 0);

    public static PlaylistGeneratorResult Ok(
        IReadOnlyList<AudioCandidate> tracks,
        int totalCandidates,
        int skippedUnreadable,
        int filteredByTags,
        int filteredByDuration,
        int relaxedByArtistRules) =>
        new(true, null, tracks, totalCandidates, skippedUnreadable,
            filteredByTags, filteredByDuration, relaxedByArtistRules);
}

/// <summary>
/// Servicio de generacion automatica de playlists.
///
/// Uso tipico desde la UI:
///
///     var progress = new Progress&lt;PlaylistGeneratorProgress&gt;(p =&gt; ...);
///     var options  = new PlaylistGeneratorOptions { ... };
///     var result   = await Task.Run(() =&gt;
///         PlaylistGeneratorService.Generate(options, progress, ct));
///
/// El metodo Generate es SINCRONICO. El caller debe envolverlo en
/// Task.Run si lo invoca desde el hilo de UI.
///
/// Estrategia de cache:
///   - Duraciones: se cachean siempre.
///   - Tags: se leen SOLO si hay filtros o reglas de variedad activas.
///     Se cachean tambien.
/// </summary>
public static class PlaylistGeneratorService
{
    private sealed class CachedInfo
    {
        public double Duration;
        public AudioTagInfo? Tags;
    }

    private static readonly ConcurrentDictionary<string, CachedInfo> _infoCache
        = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public static void ClearCache() => _infoCache.Clear();

    // ───── API principal ────────────────────────────────────────────────

    public static PlaylistGeneratorResult Generate(
        PlaylistGeneratorOptions options,
        IProgress<PlaylistGeneratorProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (options is null)
            return PlaylistGeneratorResult.Fail("Configuracion vacia.");

        if (string.IsNullOrWhiteSpace(options.SourceFolder))
            return PlaylistGeneratorResult.Fail("No se especifico una carpeta de origen.");

        if (!Directory.Exists(options.SourceFolder))
            return PlaylistGeneratorResult.Fail("La carpeta seleccionada no existe.");

        bool needTags = options.HasActiveTagFilters;

        // ── 1) Enumerar archivos ────────────────────────────────────────
        progress?.Report(new PlaylistGeneratorProgress("Enumerando archivos...", 0, 0));

        var files = EnumerateAudioFiles(options.SourceFolder, options.IncludeSubfolders, ct);

        if (files.Count == 0)
            return PlaylistGeneratorResult.Fail("No se encontraron archivos de audio en la carpeta.");

        AppLogger.Info("PLAYLIST-GEN",
            $"Escaneando '{options.SourceFolder}': {files.Count} archivo(s) candidato(s). " +
            $"Filtros activos: {(options.HasAnyFilter ? "si" : "no")} | " +
            $"Variedad activa: {(options.HasActiveVarietyRules ? "si" : "no")} | " +
            $"Lectura de tags: {(needTags ? "si" : "no")}.");

        // ── 2) Leer duraciones (y tags si hace falta) ───────────────────
        var candidates = new List<AudioCandidate>(files.Count);
        int skipped = 0;
        int filteredByTags = 0;
        int filteredByDuration = 0;
        int processed = 0;

        string progressLabel = needTags
            ? "Leyendo duraciones y tags..."
            : "Leyendo duraciones...";

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            var info = GetOrLoadInfo(path, needTags, ct);

            if (info.Duration <= 0.5)
            {
                skipped++;
            }
            else
            {
                var candidate = new AudioCandidate
                {
                    FilePath = path,
                    DurationSeconds = info.Duration,
                    Tags = info.Tags ?? AudioTagInfo.Empty
                };

                if (!PassesDurationFilter(candidate, options))
                {
                    filteredByDuration++;
                }
                else if (needTags && !PassesTagFilters(candidate, options))
                {
                    filteredByTags++;
                }
                else
                {
                    candidates.Add(candidate);
                }
            }

            processed++;
            if (processed % 10 == 0 || processed == files.Count)
            {
                progress?.Report(new PlaylistGeneratorProgress(
                    $"{progressLabel} {processed}/{files.Count}",
                    processed, files.Count));
            }
        }

        if (candidates.Count == 0)
        {
            if (options.HasAnyFilter)
            {
                return PlaylistGeneratorResult.Fail(
                    $"Ninguna pista cumple con los filtros activos. " +
                    $"(descartadas por filtros: {filteredByTags + filteredByDuration})");
            }

            return PlaylistGeneratorResult.Fail("Ningun archivo se pudo leer correctamente.");
        }

        // ── 3) Seleccion segun reglas ───────────────────────────────────
        progress?.Report(new PlaylistGeneratorProgress("Armando la playlist...", 0, 0));

        var selected = SelectTracks(candidates, options, ct, out int relaxedCount);

        AppLogger.Info("PLAYLIST-GEN",
            $"Generadas {selected.Count} pista(s) desde {candidates.Count} candidato(s) validos " +
            $"(descartados: {skipped} ilegibles, {filteredByTags} por tags, " +
            $"{filteredByDuration} por duracion, {relaxedCount} relajadas por variedad).");

        return PlaylistGeneratorResult.Ok(
            selected,
            candidates.Count,
            skipped,
            filteredByTags,
            filteredByDuration,
            relaxedCount);
    }

    // ───── Paso 1: enumeracion ──────────────────────────────────────────

    private static List<string> EnumerateAudioFiles(string folder, bool includeSubdirs, CancellationToken ct)
    {
        var result = new List<string>();

        var searchOpt = includeSubdirs
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.*", searchOpt))
            {
                ct.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) continue;

                if (IsSupportedAudioExtension(ext))
                    result.Add(path);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.Warning("PLAYLIST-GEN", $"Error enumerando '{folder}': {ex.Message}");
        }

        return result;
    }

    private static bool IsSupportedAudioExtension(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".mp3" or ".mp2" or ".mp1" or
            ".ogg" or ".oga" or
            ".wav" or ".aif" or ".aiff" or
            ".flac" or ".fla" or
            ".m4a" or ".m4b" or ".aac" or ".mp4" or
            ".opus" or ".wv" or
            ".mo3" or ".mod" or ".xm" or ".it" or ".s3m" or ".mtm" or ".umx" => true,
            _ => false
        };
    }

    // ───── Paso 2: lectura con cache ────────────────────────────────────

    private static CachedInfo GetOrLoadInfo(string path, bool loadTags, CancellationToken ct)
    {
        var info = _infoCache.GetOrAdd(path, _ => new CachedInfo());

        lock (info)
        {
            if (info.Duration <= 0)
            {
                try { info.Duration = AudioFileInfoReader.GetDurationSeconds(path); }
                catch { info.Duration = 0; }
            }

            if (loadTags && info.Tags is null)
            {
                try { info.Tags = AudioTagReader.Read(path); }
                catch { info.Tags = AudioTagInfo.Empty; }
            }
        }

        return info;
    }

    // ───── Paso 2b: filtros ─────────────────────────────────────────────

    private static bool PassesDurationFilter(AudioCandidate c, PlaylistGeneratorOptions opts)
    {
        if (!opts.IsDurationRangeFilterEnabled) return true;

        double minSec = Math.Max(0, opts.FilterMinDurationMinutes) * 60.0;
        double maxSec = Math.Max(0, opts.FilterMaxDurationMinutes) * 60.0;

        if (maxSec > 0 && maxSec < minSec)
            (minSec, maxSec) = (maxSec, minSec);

        if (c.DurationSeconds < minSec) return false;
        if (maxSec > 0 && c.DurationSeconds > maxSec) return false;

        return true;
    }

    private static bool PassesTagFilters(AudioCandidate c, PlaylistGeneratorOptions opts)
    {
        var tags = c.Tags ?? AudioTagInfo.Empty;

        if (!string.IsNullOrWhiteSpace(opts.FilterGenre))
        {
            if (string.IsNullOrWhiteSpace(tags.Genres)) return false;
            if (!tags.Genres.Contains(opts.FilterGenre, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(opts.FilterArtist))
        {
            if (string.IsNullOrWhiteSpace(tags.Performers)) return false;
            if (!tags.Performers.Contains(opts.FilterArtist, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(opts.FilterAlbum))
        {
            if (string.IsNullOrWhiteSpace(tags.Album)) return false;
            if (!tags.Album.Contains(opts.FilterAlbum, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (opts.IsYearRangeFilterEnabled)
        {
            int y = tags.Year;
            if (y <= 0) return false;

            int minY = Math.Min(opts.FilterYearMin, opts.FilterYearMax);
            int maxY = Math.Max(opts.FilterYearMin, opts.FilterYearMax);

            if (y < minY) return false;
            if (y > maxY) return false;
        }

        return true;
    }

    // ───── Paso 3: seleccion ────────────────────────────────────────────

    /// <summary>
    /// Selecciona las pistas aplicando orden, modo (cantidad/duracion) y
    /// reglas de variedad (ventana deslizante de artistas).
    ///
    /// Si las reglas de variedad impiden llegar al objetivo y
    /// <see cref="PlaylistGeneratorOptions.RelaxIfNotEnough"/> esta activo,
    /// hace una segunda pasada aceptando las pistas descartadas por
    /// variedad, marcandolas en <paramref name="relaxedCount"/>.
    /// </summary>
    private static List<AudioCandidate> SelectTracks(
        IReadOnlyList<AudioCandidate> candidates,
        PlaylistGeneratorOptions options,
        CancellationToken ct,
        out int relaxedCount)
    {
        relaxedCount = 0;

        var pool = new List<AudioCandidate>(candidates);

        // ── 3a) Orden inicial del pool ──────────────────────────────────
        if (options.BalanceByArtist)
        {
            // Round-robin: intercalar artistas para maxima distribucion.
            pool = BuildRoundRobinPool(pool);
        }
        else
        {
            switch (options.Order)
            {
                case PlaylistGeneratorOrder.AlphabeticalByName:
                    pool.Sort((a, b) => string.Compare(
                        Path.GetFileName(a.FilePath),
                        Path.GetFileName(b.FilePath),
                        StringComparison.OrdinalIgnoreCase));
                    break;

                case PlaylistGeneratorOrder.AlphabeticalByPath:
                    pool.Sort((a, b) => string.Compare(
                        a.FilePath, b.FilePath,
                        StringComparison.OrdinalIgnoreCase));
                    break;

                case PlaylistGeneratorOrder.Random:
                default:
                    Shuffle(pool);
                    break;
            }
        }

        // ── 3b) Definir ventana de artistas ─────────────────────────────
        int artistWindow = options.EffectiveArtistWindow;
        bool hasVarietyRules = artistWindow > 0;

        // ── 3c) Definir targets ─────────────────────────────────────────
        bool isTrackCountMode = options.Mode == PlaylistGeneratorMode.TrackCount;

        int targetCount = isTrackCountMode
            ? Math.Max(1, options.TargetTrackCount)
            : int.MaxValue;

        double targetSeconds = isTrackCountMode
            ? double.MaxValue
            : Math.Max(60.0, options.TargetDurationMinutes * 60.0);

        double toleranceSeconds = isTrackCountMode
            ? 0
            : Math.Max(0.0, options.ToleranceMinutes * 60.0);

        double lowBound = isTrackCountMode ? double.MaxValue : Math.Max(0.0, targetSeconds - toleranceSeconds);
        double highBound = isTrackCountMode ? double.MaxValue : targetSeconds + toleranceSeconds;

        // ── 3d) Primera pasada con reglas aplicadas ─────────────────────
        var selected = new List<AudioCandidate>();
        var skippedByVariety = new List<AudioCandidate>();
        var recentArtists = new Queue<string>(Math.Max(1, artistWindow));
        double totalDuration = 0;

        foreach (var c in pool)
        {
            ct.ThrowIfCancellationRequested();

            // Corte temprano si ya alcanzamos.
            if (ReachedTarget(selected.Count, totalDuration, isTrackCountMode,
                              targetCount, lowBound))
                break;

            if (PassesVarietyRules(c, recentArtists, artistWindow, hasVarietyRules))
            {
                if (FitsInTarget(c, isTrackCountMode, highBound, totalDuration,
                                 selected.Count, targetCount))
                {
                    selected.Add(c);
                    totalDuration += c.DurationSeconds;
                    PushArtist(recentArtists, c.Artist, artistWindow);
                }
            }
            else
            {
                // Guardar para la segunda pasada.
                skippedByVariety.Add(c);
            }
        }

        // ── 3e) Segunda pasada de relajacion (opcional) ────────────────
        if (options.RelaxIfNotEnough && hasVarietyRules && skippedByVariety.Count > 0)
        {
            bool stillNeedMore = !ReachedTarget(selected.Count, totalDuration,
                                                isTrackCountMode, targetCount, lowBound);

            if (stillNeedMore)
            {
                // Ordenar los descartados por duracion para no romper la
                // ventana de tolerancia en modo duracion.
                if (!isTrackCountMode)
                    skippedByVariety.Sort((a, b) => a.DurationSeconds.CompareTo(b.DurationSeconds));

                foreach (var c in skippedByVariety)
                {
                    ct.ThrowIfCancellationRequested();

                    if (ReachedTarget(selected.Count, totalDuration, isTrackCountMode,
                                      targetCount, lowBound))
                        break;

                    if (FitsInTarget(c, isTrackCountMode, highBound, totalDuration,
                                     selected.Count, targetCount))
                    {
                        selected.Add(c);
                        totalDuration += c.DurationSeconds;
                        relaxedCount++;

                        // Nota: NO actualizamos recentArtists porque ya no
                        // estamos aplicando las reglas en esta pasada.
                    }
                }

                if (relaxedCount > 0)
                {
                    AppLogger.Info("PLAYLIST-GEN",
                        $"Segunda pasada: {relaxedCount} pista(s) agregada(s) relajando reglas de variedad.");
                }
            }
        }

        return selected;
    }

    // ───── Helpers de targets ───────────────────────────────────────────

    private static bool ReachedTarget(
        int currentCount,
        double currentDuration,
        bool isTrackCountMode,
        int targetCount,
        double lowBound)
    {
        if (isTrackCountMode)
            return currentCount >= targetCount;

        return currentDuration >= lowBound;
    }

    private static bool FitsInTarget(
        AudioCandidate c,
        bool isTrackCountMode,
        double highBound,
        double currentDuration,
        int currentCount,
        int targetCount)
    {
        if (isTrackCountMode)
            return currentCount < targetCount;

        // Modo duracion: no pasarse del techo.
        return currentDuration + c.DurationSeconds <= highBound;
    }

    // ───── Helpers de variedad ──────────────────────────────────────────

    private static bool PassesVarietyRules(
        AudioCandidate c,
        Queue<string> recentArtists,
        int artistWindow,
        bool hasVarietyRules)
    {
        if (!hasVarietyRules) return true;
        if (!c.HasArtist) return true; // wildcard: nunca bloquea

        // Si el artista esta en la ventana reciente, no pasa.
        if (recentArtists.Contains(c.Artist)) return false;

        return true;
    }

    private static void PushArtist(Queue<string> recentArtists, string artist, int windowSize)
    {
        if (windowSize <= 0) return;
        if (string.IsNullOrEmpty(artist)) return; // wildcard: no ocupa lugar en la ventana

        recentArtists.Enqueue(artist);

        while (recentArtists.Count > windowSize)
            recentArtists.Dequeue();
    }

    // ───── Round-robin por artista ──────────────────────────────────────

    /// <summary>
    /// Reordena el pool intercalando artistas:
    ///   - Agrupa por artista (los "sin artista" forman un grupo wildcard).
    ///   - Ordena los grupos: primero los que tienen artista, y dentro de
    ///     ellos por cantidad DESCENDENTE (los mas frecuentes primero, para
    ///     que se repartan mejor).
    ///   - Shuffle dentro de cada grupo.
    ///   - Intercala uno de cada grupo en rotacion.
    /// </summary>
    private static List<AudioCandidate> BuildRoundRobinPool(List<AudioCandidate> input)
    {
        if (input.Count == 0) return new List<AudioCandidate>();

        // Agrupar por artista. Clave "" = wildcard.
        var groups = input
            .GroupBy(c => c.Artist, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list = g.ToList();
                Shuffle(list);
                return new
                {
                    Key = g.Key,
                    IsWildcard = string.IsNullOrEmpty(g.Key),
                    Count = list.Count,
                    Items = new Queue<AudioCandidate>(list)
                };
            })
            .OrderBy(g => g.IsWildcard ? 1 : 0)
            .ThenByDescending(g => g.Count)
            .ToList();

        // Intercalar: una pista de cada grupo por vuelta.
        var result = new List<AudioCandidate>(input.Count);

        while (groups.Any(g => g.Items.Count > 0))
        {
            foreach (var g in groups)
            {
                if (g.Items.Count > 0)
                    result.Add(g.Items.Dequeue());
            }
        }

        return result;
    }

    /// <summary>
    /// Fisher-Yates shuffle usando Random.Shared (thread-safe).
    /// </summary>
    private static void Shuffle<T>(IList<T> list)
    {
        var rng = Random.Shared;
        int n = list.Count;
        while (n > 1)
        {
            int k = rng.Next(n--);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }
}
