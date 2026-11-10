using System.Text.Json;

namespace RadioMaker.UI.Configuration;

/// <summary>
/// Estado persistente del Explorador de archivos.
/// </summary>
public sealed class FileSystemBrowserState
{
    public int Version { get; set; } = 1;

    /// <summary>Ultima carpeta navegada. Vacio si nunca se navego.</summary>
    public string LastFolder { get; set; } = string.Empty;
}

/// <summary>
/// Persistencia del estado del Explorador de archivos en
/// %APPDATA%/RmsAudioPlayer/filesystem-browser.json.
/// </summary>
public static class FileSystemBrowserStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    public static FileSystemBrowserState Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var state = JsonSerializer.Deserialize<FileSystemBrowserState>(json, Options);
                if (state is not null) return state;
            }
        }
        catch { }
        return new FileSystemBrowserState();
    }

    public static void Save(FileSystemBrowserState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "filesystem-browser.json");
    }
}