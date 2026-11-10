using System.Text.Json;
using System.Text.Json.Serialization;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Configuration;

/// <summary>
/// Persiste la configuración de buses (nombre, dispositivo, volumen master)
/// en un archivo JSON bajo el directorio de datos del usuario.
///
/// Ubicación por SO:
///   • Windows: %APPDATA%\RmsAudioPlayer\buses.json
///   • macOS:   ~/Library/Application Support/RmsAudioPlayer/buses.json
///   • Linux:   ~/.config/RmsAudioPlayer/buses.json
/// </summary>
public static class BusConfigurationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Ruta completa del archivo de configuración.</summary>
    public static string ConfigPath { get; } = BuildConfigPath();

    /// <summary>
    /// Carga la configuración de buses. Si el archivo no existe devuelve una
    /// lista con un único bus primario apuntando al dispositivo por defecto.
    /// </summary>
    public static IReadOnlyList<AudioBusConfig> Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var list = JsonSerializer.Deserialize<List<AudioBusConfig>>(json, Options);
                if (list is { Count: > 0 })
                {
                    // Garantizar que exista exactamente un bus primario.
                    if (!list.Any(b => b.IsPrimary))
                        list[0].IsPrimary = true;

                    return list;
                }
            }
        }
        catch
        {
            // Si la config está corrupta, devolvemos la de por defecto.
        }

        return new List<AudioBusConfig>
        {
            new()
            {
                Id = "primary",
                DisplayName = "Principal",
                DeviceIndex = -1,
                MasterVolume = 1.0,
                IsPrimary = true
            }
        };
    }

    /// <summary>Guarda la configuración de buses en disco.</summary>
    public static void Save(IEnumerable<AudioBusConfig> buses)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(buses.ToList(), Options);
        File.WriteAllText(ConfigPath, json);
    }

    private static string BuildConfigPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(root, "RadioMaker", "buses.json");
    }
}
