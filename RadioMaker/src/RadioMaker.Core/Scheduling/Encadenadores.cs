using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

public enum EncadenadorOrder
{
    Sequential = 0,
    Random = 1
}

public enum EncadenadorCounterReset
{
    Continuous = 0,
    OnSourceChange = 1
}

public sealed class EncadenadorTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Title;
}

/// -------------Lista independiente y configuración de una pestaña Encadenadores.</summary>
public sealed class EncadenadoresSet
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Encadenadores";
    public List<EncadenadorTrack> Tracks { get; set; } = new();
    public EncadenadorOrder Order { get; set; } = EncadenadorOrder.Sequential;
    public int InsertEvery { get; set; } = 1;
    public EncadenadorCounterReset CounterReset { get; set; } = EncadenadorCounterReset.OnSourceChange;
    [JsonIgnore]
    public bool IsActive { get; set; }
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
