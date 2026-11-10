using System.IO;

namespace RadioMaker.UI.Services;

/// <summary>
/// Migra los datos de la carpeta vieja (%APPDATA%/RmsAudioPlayer) a la
/// nueva (%APPDATA%/RadioMaker) la primera vez que se arranca despues
/// del renombrado de la app.
///
/// Reglas:
///   - Si no existe la carpeta vieja, no hace nada.
///   - Si la carpeta nueva ya tiene contenido, no hace nada (evita pisar
///     datos mas recientes).
///   - Si la nueva esta vacia o no existe, copia TODO recursivamente.
///
/// Se llama desde Program.Main() ANTES de cualquier otro Load, para que
/// todos los Store encuentren sus archivos ya migrados.
/// </summary>
public static class UserDataMigrator
{
    private const string OldFolderName = "RmsAudioPlayer";
    private const string NewFolderName = "RadioMaker";

    /// <summary>
    /// Ejecuta la migracion si corresponde. Es idempotente: correrlo
    /// varias veces no hace dano.
    /// </summary>
    public static void MigrateIfNeeded()
    {
        try
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);

            if (string.IsNullOrEmpty(root)) return;

            var oldDir = Path.Combine(root, OldFolderName);
            var newDir = Path.Combine(root, NewFolderName);

            // Sin carpeta vieja, no hay nada que migrar.
            if (!Directory.Exists(oldDir)) return;

            // La carpeta nueva ya tiene contenido: no tocamos nada.
            if (Directory.Exists(newDir) &&
                Directory.EnumerateFileSystemEntries(newDir).Any())
                return;

            // Crear la nueva y copiar todo.
            Directory.CreateDirectory(newDir);
            CopyDirectoryRecursive(oldDir, newDir);
        }
        catch
        {
            // Best-effort: si falla, la app arranca con la carpeta nueva vacia.
        }
    }

    private static void CopyDirectoryRecursive(string source, string dest)
    {
        // Subdirectorios.
        try
        {
            foreach (var dir in Directory.GetDirectories(source))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;

                var newSub = Path.Combine(dest, name);
                Directory.CreateDirectory(newSub);
                CopyDirectoryRecursive(dir, newSub);
            }
        }
        catch { }

        // Archivos.
        try
        {
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name)) continue;

                var destFile = Path.Combine(dest, name);
                try
                {
                    // No sobreescribir si ya existe (defensa extra).
                    if (!File.Exists(destFile))
                        File.Copy(file, destFile, overwrite: false);
                }
                catch { }
            }
        }
        catch { }
    }
}