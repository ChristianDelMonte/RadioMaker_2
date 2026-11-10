using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.UI.Configuration;

/// <summary>
/// Preferencia de tema elegida por el usuario.
/// </summary>
public enum ThemePreference
{
    /// <summary>Sigue el tema del sistema operativo (por defecto).</summary>
    System = 0,

    /// <summary>Tema claro siempre.</summary>
    Light = 1,

    /// <summary>Tema oscuro siempre.</summary>
    Dark = 2
}

/// <summary>
/// Persistencia de la preferencia de tema en
/// %APPDATA%/RmsAudioPlayer/theme.json.
/// </summary>
public static class ThemeSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static ThemePreference Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var wrapper = JsonSerializer.Deserialize<ThemeState>(json, Options);
                if (wrapper is not null) return wrapper.Preference;
            }
        }
        catch { }
        return ThemePreference.Dark;   // default hasta terminar la migracion
    }

    public static void Save(ThemePreference pref)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var state = new ThemeState { Preference = pref };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "theme.json");
    }

    private sealed class ThemeState
    {
        public int Version { get; set; } = 1;
        public ThemePreference Preference { get; set; } = ThemePreference.Dark;
    }
}