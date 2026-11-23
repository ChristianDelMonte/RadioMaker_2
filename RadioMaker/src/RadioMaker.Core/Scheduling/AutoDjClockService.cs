namespace RadioMaker.Core.Scheduling;

/// <summary>Genera una vista previa a partir del orden cíclico de espacios del reloj.</summary>
public static class AutoDjClockService
{
    public static AutoDjClockResult Generate(
        AutoDjClock clock,
        IEnumerable<string>? supportedExtensions = null,
        IProgress<PlaylistGeneratorProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (clock is null) return Fail("No hay un reloj configurado.");
        if (clock.Slots is null || clock.Slots.Count == 0)
            return Fail("Agrega al menos un espacio al reloj.");
        if (clock.TargetDurationMinutes is < 5 or > AutoDjClock.MaxTargetDurationMinutes)
            return Fail("La duración objetivo debe estar entre 5 minutos y 24 horas.");

        var scan = PlaylistGeneratorService.ScanLibrary(
            clock.SourceFolder,
            clock.IncludeSubfolders,
            supportedExtensions,
            progress,
            cancellationToken);
        if (!scan.Success)
            return Fail(scan.ErrorMessage ?? "No se pudo leer la biblioteca.");

        var candidates = scan.Candidates;
        progress?.Report(new PlaylistGeneratorProgress("Biblioteca analizada. Organizando categorías del reloj...", 0, 0));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var selectedPaths = new HashSet<string>(comparer);
        var recentArtists = new Queue<string>();
        var tracks = new List<AutoDjClockTrack>();
        int skippedSlots = 0;
        int minBetween = Math.Max(0, clock.MinTracksBetweenSameArtist);
        double targetSeconds = clock.TargetDurationMinutes * 60.0;
        double duration = 0;
        var random = new Random();

        var pools = new List<List<AudioCandidate>>(clock.Slots.Count);
        for (int slotIndex = 0; slotIndex < clock.Slots.Count; slotIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = clock.Slots[slotIndex];
            var matching = candidates
                .Where(candidate => MatchesGenre(candidate, slot.Genre))
                .OrderBy(_ => random.Next())
                .ToList();
            pools.Add(matching);
            progress?.Report(new PlaylistGeneratorProgress(
                $"Organizando categorías {slotIndex + 1}/{clock.Slots.Count}...",
                slotIndex + 1,
                clock.Slots.Count));
        }
        progress?.Report(new PlaylistGeneratorProgress("Generando la secuencia AutoDJ...", 0, 0));
        var cursors = new int[pools.Count];
        var deferredBySlot = Enumerable.Range(0, pools.Count)
            .Select(_ => new List<AudioCandidate>())
            .ToList();

        while (duration < targetSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool addedThisCycle = false;

            for (int slotIndex = 0; slotIndex < pools.Count && duration < targetSeconds; slotIndex++)
            {
                var pool = pools[slotIndex];
                AudioCandidate? chosen = null;
                while (cursors[slotIndex] < pool.Count)
                {
                    var candidate = pool[cursors[slotIndex]++];
                    if (selectedPaths.Contains(candidate.FilePath)) continue;
                    if (!PassesArtistSeparation(candidate, recentArtists, minBetween))
                    {
                        deferredBySlot[slotIndex].Add(candidate);
                        continue;
                    }
                    chosen = candidate;
                    break;
                }

                if (chosen is null)
                {
                    skippedSlots++;
                    continue;
                }

                selectedPaths.Add(chosen.FilePath);
                tracks.Add(new AutoDjClockTrack(chosen, clock.Slots[slotIndex].Genre?.Trim() ?? string.Empty, slotIndex + 1));
                duration += chosen.DurationSeconds;
                if (minBetween > 0 && chosen.HasArtist)
                {
                    recentArtists.Enqueue(chosen.Artist);
                    while (recentArtists.Count > minBetween) recentArtists.Dequeue();
                }
                addedThisCycle = true;
            }

            if (!addedThisCycle) break;
            for (int slotIndex = 0; slotIndex < pools.Count; slotIndex++)
            {
                if (deferredBySlot[slotIndex].Count == 0) continue;
                pools[slotIndex].AddRange(deferredBySlot[slotIndex]);
                deferredBySlot[slotIndex].Clear();
            }
        }

        if (tracks.Count == 0)
            return new(false, "No hay pistas que coincidan con los géneros del reloj.", tracks, candidates.Count, scan.SkippedUnreadable, skippedSlots, targetSeconds);

        return new(true, null, tracks, candidates.Count, scan.SkippedUnreadable, skippedSlots, targetSeconds);
    }

    private static bool MatchesGenre(AudioCandidate candidate, string? requestedGenre)
    {
        if (string.IsNullOrWhiteSpace(requestedGenre)) return true;
        if (string.IsNullOrWhiteSpace(candidate.Tags?.Genres)) return false;

        return candidate.Tags.Genres
            .Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(genre => string.Equals(genre, requestedGenre.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool PassesArtistSeparation(
        AudioCandidate candidate,
        Queue<string> recentArtists,
        int minimumTracksBetween)
    {
        if (minimumTracksBetween <= 0 || !candidate.HasArtist) return true;
        return !recentArtists.Contains(candidate.Artist, StringComparer.OrdinalIgnoreCase);
    }

    private static AutoDjClockResult Fail(string message) =>
        new(false, message, Array.Empty<AutoDjClockTrack>(), 0, 0, 0, 0);
}
