using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.UI.Services;

/// <summary>
/// Persistencia de carpetas favoritas en
/// %APPDATA%/RmsAudioPlayer/favorite-folders.json.
/// </summary>
public static class FavoriteFolderStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static List<FavoriteFolderEntry> Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new List<FavoriteFolderEntry>();

            var json = File.ReadAllText(ConfigPath);
            var wrapper = JsonSerializer.Deserialize<FavoritesFile>(json, Options);
            if (wrapper?.Items is null) return new List<FavoriteFolderEntry>();

            return wrapper.Items
                .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .ToList();
        }
        catch
        {
            return new List<FavoriteFolderEntry>();
        }
    }

    public static void Save(IEnumerable<FavoriteFolderEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var wrapper = new FavoritesFile
            {
                Version = 1,
                Items = entries.ToList()
            };

            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(wrapper, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "favorite-folders.json");
    }

    /// <summary>Estructura de archivo para permitir versionado futuro.</summary>
    private sealed class FavoritesFile
    {
        public int Version { get; set; } = 1;
        public List<FavoriteFolderEntry> Items { get; set; } = new();
    }
}