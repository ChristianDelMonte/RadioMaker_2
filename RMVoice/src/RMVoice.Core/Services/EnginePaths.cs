namespace RMVoice.Core.Services;

/// -------------
/// Resolución de rutas por defecto del Engine.
/// Se encarga de la migración silenciosa desde la carpeta heredada
/// (%AppData%/AnnouncerClock) hacia la nueva (%AppData%/RMVoice).
/// -------------
internal static class EnginePaths
{
    private const string AppFolderName = "RMVoice";
    private const string LegacyFolderName = "RMVoice";

    /// -------------Carpeta raíz de datos de la app (creada si no existe).
    public static string RootDirectory => EnsureRootDirectory();

    /// -------------Ruta por defecto a profiles.json.
    public static string DefaultProfilesPath()
        => Path.Combine(EnsureRootDirectory(), "profiles.json");

    /// -------------Ruta por defecto a settings.json.
    public static string DefaultSettingsPath()
        => Path.Combine(EnsureRootDirectory(), "settings.json");

    private static string EnsureRootDirectory()
    {
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseFolder))
            baseFolder = AppContext.BaseDirectory;

        var newDir = Path.Combine(baseFolder, AppFolderName);
        var legacyDir = Path.Combine(baseFolder, LegacyFolderName);

        // Migración silenciosa: si existe la carpeta vieja y no la nueva, la movemos.
        if (Directory.Exists(legacyDir) && !Directory.Exists(newDir))
        {
            try { Directory.Move(legacyDir, newDir); }
            catch { /* si falla, seguimos con la carpeta nueva vacía */ }
        }

        Directory.CreateDirectory(newDir);
        return newDir;
    }
}