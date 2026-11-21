using System.Text.Json;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Persistencia del volumen master global de la aplicacion en
/// %APPDATA%/RmsAudioPlayer/volume.json.
/// -------------
public static class VolumeSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    /// -------------Carga el volumen guardado. Devuelve 1.0 si no existe o falla.</summary>
    public static double Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var state = JsonSerializer.Deserialize<VolumeState>(json, Options);
                if (state is not null)
                    return Math.Clamp(state.Volume, 0.0, 1.0);
            }
        }
        catch { }

        return 1.0;
    }

    public static void Save(double volume)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var state = new VolumeState { Volume = Math.Clamp(volume, 0.0, 1.0) };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "volume.json");
    }

    private sealed class VolumeState
    {
        public int Version { get; set; } = 1;
        public double Volume { get; set; } = 1.0;
    }
}