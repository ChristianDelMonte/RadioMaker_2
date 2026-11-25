using System.Diagnostics;
using System.IO;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.Services;

/// -------------
/// Helper estático para lanzar la app RMWave con un archivo .rmw
/// abierto en la UI. Reutiliza la misma lógica de búsqueda del .exe que
/// <c>MainWindow.LaunchComplement</c> (Addons/RMWave/RMWave.exe, etc).
///
/// IMPORTANTE: cada llamada abre una NUEVA instancia de RMWave. Si el
/// usuario ya tenía RMWave abierto, va a tener dos ventanas. Ver nota de
/// limitaciones al pie del README de integración.
/// -------------
public static class RMWaveLauncher
{
    /// -------------True si el path es un proyecto .rmw.</summary>
    public static bool IsRmwFile(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);

    /// -------------
    /// Abre el .rmw en RMWave UI. Devuelve false si no se encontró el .exe
    /// o si el path no es válido (con un mensaje de error opcional).
    /// -------------
    public static bool OpenFile(string? filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = LocalizationService.GetString("Loc.RMWaveLauncher.PathEmpty");
            return false;
        }

        if (!IsRmwFile(filePath))
        {
            errorMessage = string.Format(LocalizationService.GetString("Loc.RMWaveLauncher.InvalidProject"), Path.GetFileName(filePath));
            return false;
        }

        if (!File.Exists(filePath))
        {
            errorMessage = string.Format(LocalizationService.GetString("Loc.RMWaveLauncher.FileMissing"), Path.GetFileName(filePath));
            return false;
        }

        var exePath = FindRMWaveExe();
        if (exePath is null)
        {
            errorMessage = LocalizationService.GetString("Loc.RMWaveLauncher.AddonMissing");
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

            errorMessage = string.Format(LocalizationService.GetString("Loc.RMWaveLauncher.OpenError"), ex.Message);
            return false;
        }
    }

    /// -------------Busca el ejecutable de RMWave en las ubicaciones conocidas.</summary>
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
