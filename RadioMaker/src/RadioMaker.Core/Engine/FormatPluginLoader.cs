using System.Runtime.InteropServices;
using ManagedBass;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.Core.Engine;

/// -------------
/// Escanea el directorio de la app buscando DLLs de plugins de BASS
/// (bass*.dll / libbass*.so / libbass*.dylib), las carga automaticamente,
/// y expone las extensiones de archivo soportadas por cada una.
///
/// Sigue el patron del ejemplo oficial plugins.c de Un4seen:
///   1. Buscar archivos que matcheen bass*.{dll,so,dylib} en el directorio.
///   2. Bass.PluginLoad(path) para cada uno.
///   3. Si el handle != 0, el plugin se cargo correctamente.
///
/// Diferencias con plugins.c:
///   - NO consultamos PluginGetInfo porque el struct PluginInfo del wrapper
///     .NET provocaba AccessViolation al marshalear los punteros nativos.
///   - En su lugar, mantenemos un diccionario de plugins conocidos con sus
///     extensiones hardcodeadas. Si aparece un plugin desconocido, igual se
///     carga y se lista, pero sin extensiones asociadas.
///
/// IMPORTANTE (fix Linux/macOS):
///   Los plugins de un4seen en Linux/macOS se distribuyen con prefijo "lib"
///   (libbassflac.so, libbass_aac.so, libbassopus.so, etc). El diccionario
///   KnownPlugins usa las claves SIN ese prefijo (bassflac, bass_aac, ...).
///   Hay que normalizar el nombre del archivo ANTES de hacer el lookup, si no
///   el plugin carga bien pero sus extensiones quedan vacías.
/// -------------
internal static class FormatPluginLoader
{
    /// -------------Metadata de cada plugin conocido.</summary>
    private sealed record PluginMetadata(string DisplayName, string[] Extensions);

    /// -------------
    /// Diccionario de plugins conocidos. Clave = nombre del archivo sin
    /// extensión NI prefijo "lib", en minúsculas. Los que no aparecen aquí
    /// se listan como "desconocidos" pero igual se cargan.
    /// -------------
    private static readonly Dictionary<string, PluginMetadata> KnownPlugins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["bassflac"] = new("FLAC", new[] { ".flac", ".fla", ".oga", ".ogg" }),
            ["bassopus"] = new("Opus", new[] { ".opus" }),
            ["bass_aac"] = new("AAC", new[] { ".m4a", ".m4b", ".aac", ".mp4" }),
            ["basswv"] = new("WavPack", new[] { ".wv" }),
            ["bassalac"] = new("ALAC", new[] { ".alac", ".caf" }),
            ["bassape"] = new("Monkey's Audio", new[] { ".ape" }),
            ["bass_mpc"] = new("Musepack", new[] { ".mpc", ".mp+", ".mpp" }),
            ["bass_spx"] = new("Speex", new[] { ".spx" }),
            ["bass_ac3"] = new("AC-3", new[] { ".ac3" }),
            ["bassdsd"] = new("DSD", new[] { ".dsf", ".dff" }),
            ["basswebm"] = new("WebM", new[] { ".webm", ".weba" }),
            ["basswma"] = new("Windows Media", new[] { ".wma", ".asf" }),
            ["bassmidi"] = new("MIDI", new[] { ".mid", ".midi" }),
            ["basshls"] = new("HLS", new[] { ".m3u8" }),
        };

    /// -------------Nombres de archivos que NO son plugins de formato.</summary>
    private static readonly string[] NonPluginNames = { "bass", "bass_fx" };

    // -----------------------------------------------------------------------

    public static IReadOnlyList<FormatPluginInfo> LoadAll()
    {
        var result = new List<FormatPluginInfo>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppLogger.Debug("PLUGINS", "Escaneando directorios buscando addons de formato...");

        foreach (var dir in GetSearchDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in FindBassPluginFiles(dir))
            {
                // ===== FIX Linux/macOS =====
                // El nombre normalizado (sin "lib") es el que matchea el
                // diccionario KnownPlugins. Antes se usaba el nombre del
                // archivo SIN normalizar (con "lib"), y el TryGetValue
                // fallaba, dejando extensiones vacías.
                var baseName = GetPluginBaseName(file);
                if (!seenNames.Add(baseName)) continue;

                result.Add(TryLoad(baseName, file));
            }
        }

        int loaded = result.Count(p => p.IsLoaded);
        if (result.Count == 0)
        {
            AppLogger.Debug("PLUGINS",
                "Sin addons externos. Solo formatos nativos disponibles.");
        }
        else
        {
            AppLogger.Info("PLUGINS",
                $"Addons cargados: {loaded} de {result.Count} encontrados.");
        }

        return result;
    }

    // -----------------------------------------------------------------------

    private static FormatPluginInfo TryLoad(string baseName, string filePath)
    {
        var meta = KnownPlugins.TryGetValue(baseName, out var known)
            ? known
            : new PluginMetadata(baseName, Array.Empty<string>());

        try
        {
            int handle = Bass.PluginLoad(filePath);

            if (handle == 0)
            {
                AppLogger.Warning("PLUGINS",
                    $"No se pudo cargar el addon '{baseName}' ({meta.DisplayName}). " +
                    $"BASS Error: {Bass.LastError}");
                return new FormatPluginInfo(
                    meta.DisplayName, meta.Extensions, new Version(0, 0), IsLoaded: false);
            }

            AppLogger.Debug("PLUGINS",
                $"Addon '{meta.DisplayName}' cargado correctamente " +
                $"({string.Join(", ", meta.Extensions)})");

            return new FormatPluginInfo(
                meta.DisplayName, meta.Extensions, new Version(0, 0), IsLoaded: true);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PLUGINS",
                $"Excepcion cargando el addon '{baseName}'", ex);

            return new FormatPluginInfo(
                meta.DisplayName, meta.Extensions, new Version(0, 0), IsLoaded: false);
        }
    }

    // -----------------------------------------------------------------------
    // Descubrimiento de archivos

    private static IEnumerable<string> GetSearchDirectories()
    {
        var baseDir = AppContext.BaseDirectory;
        string rid = RuntimeInformation.RuntimeIdentifier;

        // 1) Junto al ejecutable.
        yield return baseDir;

        // 2) runtimes/{rid}/native/ (donde se copian las DLLs desde el .csproj).
        yield return Path.Combine(baseDir, "runtimes", rid, "native");

        // 3) Fallback: cualquier runtimes/*/native/.
        var runtimesRoot = Path.Combine(baseDir, "runtimes");
        if (Directory.Exists(runtimesRoot))
        {
            foreach (var sub in Directory.EnumerateDirectories(runtimesRoot))
            {
                var native = Path.Combine(sub, "native");
                if (Directory.Exists(native) && !native.Equals(
                        Path.Combine(baseDir, "runtimes", rid, "native"),
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return native;
                }
            }
        }
    }

    /// -------------
    /// Encuentra todos los archivos bass*.{dll,so,dylib} en un directorio,
    /// excluyendo el core (bass) y el addon de efectos (bass_fx).
    /// -------------
    private static IEnumerable<string> FindBassPluginFiles(string directory)
    {
        string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll"
                         : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib"
                         : ".so";

        string prefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "bass" : "libbass";

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, prefix + "*" + extension);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            // Filtra usando el nombre normalizado (sin "lib").
            var baseName = GetPluginBaseName(file);

            if (NonPluginNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                continue;

            yield return file;
        }
    }

    /// -------------
    /// Devuelve el nombre del plugin normalizado, SIN extensión y SIN
    /// prefijo "lib". Ejemplos:
    ///   • Windows: "bassflac.dll"       → "bassflac"
    ///   • Linux:   "libbassflac.so"     → "bassflac"
    ///   • macOS:   "libbassflac.dylib"  → "bassflac"
    ///   • Linux:   "libbass_aac.so"     → "bass_aac"
    ///
    /// Este nombre es el que usan:
    ///   • KnownPlugins (para matchear las extensiones conocidas).
    ///   • NonPluginNames (para excluir bass.dll y bass_fx.dll).
    /// -------------
    private static string GetPluginBaseName(string filePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(filePath);

        if (baseName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[3..];

        return baseName;
    }
}