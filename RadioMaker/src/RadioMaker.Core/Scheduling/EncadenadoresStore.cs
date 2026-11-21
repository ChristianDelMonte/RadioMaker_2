using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

public static class EncadenadoresStore
{
    public const string FileExtension = ".rme";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(EncadenadoresSet set, string path)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta vacía.", nameof(path));
        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) path += FileExtension;

        set.Tracks ??= new List<EncadenadorTrack>();
        set.InsertEvery = Math.Clamp(set.InsertEvery, 1, 3);
        set.ModifiedAtUtc = DateTime.UtcNow;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(set, Options));
    }

    public static EncadenadoresSet? Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var set = JsonSerializer.Deserialize<EncadenadoresSet>(File.ReadAllText(path), Options);
            if (set is null) return null;
            set.Tracks ??= new List<EncadenadorTrack>();
            set.InsertEvery = Math.Clamp(set.InsertEvery, 1, 3);
            return set;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando .rme '{path}': {ex.Message}");
            return null;
        }
    }
}
