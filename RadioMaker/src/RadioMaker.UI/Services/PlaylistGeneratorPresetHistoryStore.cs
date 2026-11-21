using System.Text.Json;

namespace RadioMaker.UI.Services;

/// -------------
/// Entrada del historial de presets recientes del generador.
/// Solo guarda la RUTA al .rpg y un nombre cacheado para mostrar en el
/// combo sin tener que abrir cada archivo.
/// -------------
public sealed class PresetHistoryEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => DisplayName;
}

/// -------------
/// Persistencia del historial de presets recientes del generador en
/// %APPDATA%/RmsAudioPlayer/generator-presets.json.
///
/// Maximo N entradas (las mas recientes). Al cargar, se filtran las
/// entradas cuyos archivos ya no existen en disco.
/// -------------
public static class PlaylistGeneratorPresetHistoryStore
{
    private const int MaxEntries = 15;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    public static List<PresetHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new List<PresetHistoryEntry>();

            var json = File.ReadAllText(ConfigPath);
            var wrapper = JsonSerializer.Deserialize<HistoryFile>(json, Options);
            if (wrapper?.Items is null) return new List<PresetHistoryEntry>();

            // Filtrar entradas cuyos archivos ya no existen.
            return wrapper.Items
                .Where(e => !string.IsNullOrWhiteSpace(e.FilePath))
                .Where(e => File.Exists(e.FilePath))
                .OrderByDescending(e => e.LastUsedUtc)
                .Take(MaxEntries)
                .ToList();
        }
        catch
        {
            return new List<PresetHistoryEntry>();
        }
    }

    public static void Save(IEnumerable<PresetHistoryEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var wrapper = new HistoryFile
            {
                Version = 1,
                Items = entries
                    .OrderByDescending(e => e.LastUsedUtc)
                    .Take(MaxEntries)
                    .ToList()
            };

            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(wrapper, Options));
        }
        catch { }
    }

    /// -------------
    /// Agrega o actualiza una entrada del historial. La mueve al tope.
    /// Devuelve la lista actualizada.
    /// -------------
    public static List<PresetHistoryEntry> AddOrUpdate(
        IEnumerable<PresetHistoryEntry> current,
        string filePath,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return current.ToList();

        var list = current
            .Where(e => !string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        list.Insert(0, new PresetHistoryEntry
        {
            FilePath = filePath,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(filePath)
                : displayName,
            LastUsedUtc = DateTime.UtcNow
        });

        var trimmed = list.Take(MaxEntries).ToList();
        Save(trimmed);
        return trimmed;
    }

    /// -------------Quita una entrada por ruta. Devuelve la lista actualizada.</summary>
    public static List<PresetHistoryEntry> Remove(
        IEnumerable<PresetHistoryEntry> current,
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return current.ToList();

        var list = current
            .Where(e => !string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Save(list);
        return list;
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "generator-presets.json");
    }

    private sealed class HistoryFile
    {
        public int Version { get; set; } = 1;
        public List<PresetHistoryEntry> Items { get; set; } = new();
    }
}