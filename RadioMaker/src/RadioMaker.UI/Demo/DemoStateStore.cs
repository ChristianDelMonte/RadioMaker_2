using System;
using System.IO;
using System.Text.Json;

namespace RadioMaker.UI.Demo;

/// <summary>
/// Persistencia del estado de la presentación inicial.
/// Archivo: %AppData%/RadioMaker/demo-state.json
///
/// Independiente de AppSettings (que vive en RMVoice.Core y maneja clima).
/// Separar responsabilidades evita acoplar la UI de RadioMaker al modelo
/// del motor de locuciones.
/// </summary>
public static class DemoStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    /// <summary>
    /// True si el usuario ya completó la presentación inicial.
    /// Default: false (primera vez).
    /// </summary>
    public static bool IsCompleted()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return false;
            var json = File.ReadAllText(ConfigPath);
            var state = JsonSerializer.Deserialize<DemoState>(json, Options);
            return state?.Completed ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Marca la demo como completada. Llamar al arrancarla (no al terminarla)
    /// para garantizar que se vea exactamente una vez.
    /// </summary>
    public static void MarkCompleted()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var state = new DemoState { Completed = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch
        {
            // Best-effort. Si falla, la demo vuelve a aparecer la próxima vez.
        }
    }

    /// <summary>Borra el estado (para testing: permite volver a ver la demo).</summary>
    public static void Reset()
    {
        try
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "demo-state.json");
    }

    private sealed class DemoState
    {
        public int Version { get; set; } = 1;
        public bool Completed { get; set; }
    }
}