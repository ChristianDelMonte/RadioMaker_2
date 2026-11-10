using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Persistencia de presets del generador en archivos .rpg (JSON).
///
/// El formato es autocontenido: todo el preset (metadata + opciones)
/// vive en un unico archivo .rpg.
/// </summary>
public static class PlaylistGeneratorPresetStore
{
    public const string FileExtension = ".rpg";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── Save ──────────────────────────────────────────────────────────

    public static void Save(PlaylistGeneratorPreset preset, string path)
    {
        if (preset is null) throw new ArgumentNullException(nameof(preset));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            path += FileExtension;

        preset.ModifiedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(preset, Options);
        File.WriteAllText(path, json);
    }

    // ───── Load ──────────────────────────────────────────────────────────

    public static PlaylistGeneratorPreset? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var preset = JsonSerializer.Deserialize<PlaylistGeneratorPreset>(json, Options);
            if (preset is null) return null;

            preset.Options ??= new PlaylistGeneratorOptions();
            return preset;
        }
        catch
        {
            return null;
        }
    }

    // ───── Validation ────────────────────────────────────────────────────

    public static bool IsValidFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return false;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Un .rpg valido debe tener la propiedad "Options".
            return root.TryGetProperty("Options", out _);
        }
        catch
        {
            return false;
        }
    }
}