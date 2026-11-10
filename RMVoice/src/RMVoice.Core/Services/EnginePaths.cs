namespace RMVoice.Core.Services;

/// <summary>
/// Resolución de rutas por defecto del Engine.
/// Se encarga de la migración silenciosa desde la carpeta heredada
/// (%AppData%/AnnouncerClock) hacia la nueva (%AppData%/RMVoice).
/// </summary>
internal static class EnginePaths
{
    private const string AppFolderName = "RMVoice";
    private const string LegacyFolderName = "RMVoice";

    /// <summary>Carpeta raíz de datos de la app (creada si no existe).</summary>
    public static string RootDirectory => EnsureRootDirectory();

    /// <summary>Ruta por defecto a profiles.json.</summary>
    public static string DefaultProfilesPath()
        => Path.Combine(EnsureRootDirectory(), "profiles.json");

    /// <summary>Ruta por defecto a settings.json.</summary>
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