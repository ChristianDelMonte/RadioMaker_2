using System.Text.Json;
using System.Text.Json.Serialization;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Configuration;

public static class BusExclusivityConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static BusExclusivityConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<BusExclusivityConfig>(json, Options);
                if (config is not null)
                {
                    config.Policies ??= new();
                    return config;
                }
            }
        }
        catch (Exception ex) { AppLogger.Warning("SETTINGS EXCLUSIVITY", "No se pudo leer la configuración guardada.", ex); }
        return new BusExclusivityConfig();
    }

    public static void Save(BusExclusivityConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
        }
        catch (Exception ex) { AppLogger.Error("SETTINGS EXCLUSIVITY", "No se pudo guardar la configuración.", ex); }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "bus-exclusivity.json");
    }
}
