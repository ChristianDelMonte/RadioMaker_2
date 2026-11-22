using System.Text.Json;
using System.Text.Json.Serialization;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Configuration;

public static class EmergencySettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static EmergencySettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<EmergencySettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch (Exception ex) { AppLogger.Warning("SETTINGS EMERGENCY", "No se pudo leer la configuración guardada.", ex); }
        return new EmergencySettings();
    }

    public static void Save(EmergencySettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) { AppLogger.Error("SETTINGS EMERGENCY", "No se pudo guardar la configuración.", ex); }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "emergency.json");
    }
}
