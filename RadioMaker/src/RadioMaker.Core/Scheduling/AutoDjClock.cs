using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>Reloj musical del AutoDJ. Cada espacio representa un turno de una categoría.</summary>
public sealed class AutoDjClock
{
    public const int MaxTargetDurationMinutes = 24 * 60;

    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Reloj de 60 minutos";
    public string SourceFolder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public int TargetDurationMinutes { get; set; } = 60;
    public int MinTracksBetweenSameArtist { get; set; } = 3;
    public List<AutoDjClockSlot> Slots { get; set; } = new();
}

/// <summary>Un espacio del reloj. Genre vacío significa cualquier género.</summary>
public sealed class AutoDjClockSlot
{
    public string Genre { get; set; } = string.Empty;
}

public sealed record AutoDjClockTrack(AudioCandidate Candidate, string SlotGenre, int SlotNumber);

public sealed record AutoDjClockResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<AutoDjClockTrack> Tracks,
    int LibraryTrackCount,
    int SkippedUnreadable,
    int SkippedClockSlots,
    double TargetDurationSeconds)
{
    public double ActualDurationSeconds => Tracks.Sum(track => track.Candidate.DurationSeconds);
    public double DifferenceSeconds => ActualDurationSeconds - TargetDurationSeconds;
}

public static class AutoDjClockStore
{
    public const string FileExtension = ".rmclock";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(AutoDjClock clock, string path)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta vacía.", nameof(path));
        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) path += FileExtension;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(clock, JsonOptions));
    }

    public static AutoDjClock? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var clock = JsonSerializer.Deserialize<AutoDjClock>(File.ReadAllText(path), JsonOptions);
            if (clock is null) return null;
            clock.Slots ??= new List<AutoDjClockSlot>();
            return clock;
        }
        catch { return null; }
    }
}
