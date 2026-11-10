using System.Diagnostics;
using System.IO;

namespace RadioMaker.UI.Services;

/// <summary>
/// Helper estático para lanzar la app RMWave con un archivo .rmw
/// abierto en la UI. Reutiliza la misma lógica de búsqueda del .exe que
/// <c>MainWindow.LaunchComplement</c> (Addons/RMWave/RMWave.exe, etc).
///
/// IMPORTANTE: cada llamada abre una NUEVA instancia de RMWave. Si el
/// usuario ya tenía RMWave abierto, va a tener dos ventanas. Ver nota de
/// limitaciones al pie del README de integración.
/// </summary>
public static class RMWaveLauncher
{
    /// <summary>True si el path es un proyecto .rmw.</summary>
    public static bool IsRmwFile(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Abre el .rmw en RMWave UI. Devuelve false si no se encontró el .exe
    /// o si el path no es válido (con un mensaje de error opcional).
    /// </summary>
    public static bool OpenFile(string? filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = "Ruta vacía.";
            return false;
        }

        if (!IsRmwFile(filePath))
        {
            errorMessage = $"No es un proyecto RMW: {Path.GetFileName(filePath)}";
            return false;
        }

        if (!File.Exists(filePath))
        {
            errorMessage = $"El archivo no existe: {Path.GetFileName(filePath)}";
            return false;
        }

        var exePath = FindRMWaveExe();
        if (exePath is null)
        {
            errorMessage = "No se encontró RMWave. Verificá que esté en Addons/RMWave/.";
            return false;
        }

        try
        {
            // ===== CROSS-PLATFORM =====
            // UseShellExecute=false + ArgumentList funciona igual en Windows,
            // Linux y macOS sin depender de cómo cada SO interpreta los quotes.
            // ArgumentList pasa cada argumento como una unidad sin parsing,
            // así soporta rutas con espacios en los 3 SO.
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };

            psi.ArgumentList.Add("--from-radiomaker");
            psi.ArgumentList.Add("--open-file");
            psi.ArgumentList.Add(filePath);

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            // ===== LOGGING =====
            try
            {
                RadioMaker.Core.Logging.AppLogger.Error(
                    "RMWAVE-LAUNCHER",
                    $"Error al abrir RMWave con '{Path.GetFileName(filePath)}': {ex.Message}",
                    ex);
            }
            catch { }

            errorMessage = $"Error al abrir RMWave: {ex.Message}";
            return false;
        }
    }

    /// <summary>Busca el ejecutable de RMWave en las ubicaciones conocidas.</summary>
    private static string? FindRMWaveExe()
    {
        var baseDir = AppContext.BaseDirectory;

        string exeName = OperatingSystem.IsWindows() ? "RMWave.exe" : "RMWave";

        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Addons", "RMWave", exeName),
            Path.Combine(baseDir, "Complementos", exeName),
            Path.Combine(baseDir, "complementos", exeName),
            Path.Combine(baseDir, "Extensions", exeName),
            Path.Combine(baseDir, "extensions", exeName),
            Path.Combine(baseDir, exeName),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}