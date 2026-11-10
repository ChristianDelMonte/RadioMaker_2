using System.Text.Json;
using System.Text.Json.Serialization;

namespace RMVoice.UI.Configuration;

/// <summary>
/// Preferencia de tema elegida por el usuario.
/// </summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// Persistencia de la preferencia de tema de la UI de RMVoice.
/// Se guarda en %APPDATA%/RMVoice/ui-settings.json (independiente del
/// settings.json del engine, que maneja cosas de clima).
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

        // Por defecto claro (el look original de RMVoice).
        return ThemePreference.Light;
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
        return Path.Combine(root, "RMVoice", "ui-settings.json");
    }

    private sealed class ThemeState
    {
        public int Version { get; set; } = 1;
        public ThemePreference Preference { get; set; } = ThemePreference.Light;
    }
}