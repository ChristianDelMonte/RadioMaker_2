using System;
using System.IO;

namespace RadioMaker.UI.Demo;

public static class DemoStateStore
{
    public static string ConfigPath { get; } = BuildPath();

    /// <summary>
    /// True si ya se completó la presentación. Verifica la existencia
    /// del archivo marcador (no su contenido): si el archivo existe,
    /// la demo ya se mostró. Sin JSON, sin deserialización, sin riesgo
    /// de errores de parseo.
    /// </summary>
    public static bool IsCompleted() => File.Exists(ConfigPath);

    /// <summary>
    /// Marca como completada creando el archivo marcador.
    /// Se llama al ARRANCAR la demo (no al terminar), garantizando que
    /// se vea exactamente una vez incluso si el usuario cierra o crashea.
    /// </summary>
    public static bool MarkCompleted()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigPath,
                $"Demo completed at {DateTime.UtcNow:O}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Borra el archivo (para testing: permite volver a ver la demo).</summary>
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
        return Path.Combine(root, "RadioMaker", "demo-completed.flag");
    }
}