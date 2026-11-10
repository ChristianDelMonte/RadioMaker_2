using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Configuration;

/// <summary>
/// Persistencia del analisis de audio (trim + normalizacion) en archivos .rmt.
///
/// Ubicacion: %APPDATA%/RadioMaker/data/&lt;hash&gt;.rmt
///
/// Por que hash y no el nombre del archivo:
///   • Evita colisiones (dos "track.mp3" en carpetas distintas).
///   • Evita problemas con paths largos en Windows (>260 chars).
///   • El path original queda guardado adentro del .rmt para validacion.
///
/// Invalidacion automatica:
///   Si el archivo real cambio (size distinto O mtime diferente por >1s),
///   el cache se considera obsoleto y se ignora. El proximo analisis lo
///   sobreescribe.
///
/// Escritura atomica:
///   Se escribe a ".tmp" y luego se hace File.Move(overwrite: true).
///   Garantiza que nunca queda un .rmt a medio escribir.
/// </summary>
public static class AudioAnalysisCacheStore
{
    public const string Extension = ".rmt";
    private const int CurrentVersion = 1;
    private const int HashHexLength = 32;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DataFolder { get; } = BuildDataFolder();

    /// <summary>
    /// True si hay una entrada de cache valida (existe, version OK, archivo
    /// sin cambios). No lee el JSON completo — solo chequea existencia del
    /// archivo. Para el chequeo completo, usar TryLoad.
    /// </summary>
    public static bool HasValidCache(string audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath)) return false;
        try
        {
            var path = GetCachePath(audioFilePath);
            return File.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>
    /// Intenta cargar el cache. Devuelve null si:
    ///   • El audio no existe.
    ///   • No hay .rmt guardado.
    ///   • La version del .rmt no coincide.
    ///   • El audio cambio (size o mtime distinto).
    ///   • El JSON es invalido.
    /// </summary>
    public static AudioAnalysisCacheEntry? TryLoad(string audioFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(audioFilePath)) return null;
            if (!File.Exists(audioFilePath)) return null;

            var cachePath = GetCachePath(audioFilePath);
            if (!File.Exists(cachePath)) return null;

            var json = File.ReadAllText(cachePath);
            var entry = JsonSerializer.Deserialize<AudioAnalysisCacheEntry>(json, Options);

            if (entry is null) return null;
            if (entry.Version != CurrentVersion) return null;

            if (!string.Equals(entry.FullPath, audioFilePath, StringComparison.OrdinalIgnoreCase))
                return null; // hash collision (rarisimo) o archivo movido

            if (!IsFileUnchanged(audioFilePath, entry))
                return null; // archivo modificado: cache obsoleto

            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Guarda el analisis. No lanza en caso de error (best-effort): si falla
    /// el guardado, la app sigue funcionando con el analisis en memoria.
    /// </summary>
    public static bool Save(
        string audioFilePath,
        SilenceAnalysisResult silence,
        NormalizationResult levels)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(audioFilePath)) return false;
            if (!File.Exists(audioFilePath)) return false;

            var info = new FileInfo(audioFilePath);

            var entry = new AudioAnalysisCacheEntry
            {
                Version = CurrentVersion,
                FullPath = audioFilePath,
                FileSize = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Silence = silence,
                Levels = levels
            };

            Directory.CreateDirectory(DataFolder);

            var cachePath = GetCachePath(audioFilePath);
            var tmpPath = cachePath + ".tmp";

            var json = JsonSerializer.Serialize(entry, Options);

            try
            {
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                File.Move(tmpPath, cachePath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { }
                throw;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Borra el cache de un archivo concreto.
    /// </summary>
    public static void Invalidate(string audioFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(audioFilePath)) return;
            var path = GetCachePath(audioFilePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// Recorre la carpeta data/ y borra todos los .rmt cuyo audio:
    ///   • Ya no existe.
    ///   • Cambio de tamanio (probablemente reemplazado).
    ///   • Tiene JSON invalido o version distinta.
    ///
    /// Devuelve la cantidad de archivos borrados. Util para llamar
    /// manualmente desde un boton "Limpiar cache" o desde un timer
    /// periodico.
    /// </summary>
    public static int PurgeOrphans()
    {
        if (!Directory.Exists(DataFolder)) return 0;

        int deleted = 0;

        foreach (var file in Directory.EnumerateFiles(DataFolder, "*" + Extension))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<AudioAnalysisCacheEntry>(json, Options);

                if (entry is null || entry.Version != CurrentVersion ||
                    string.IsNullOrEmpty(entry.FullPath) ||
                    !File.Exists(entry.FullPath))
                {
                    File.Delete(file);
                    deleted++;
                    continue;
                }

                if (!IsFileUnchanged(entry.FullPath, entry))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch
            {
                try { File.Delete(file); deleted++; } catch { }
            }
        }

        return deleted;
    }

    /// <summary>
    /// Calcula la ruta del .rmt para un audio dado.
    /// </summary>
    public static string GetCachePath(string audioFilePath)
    {
        var hash = ComputeHash(audioFilePath);
        return Path.Combine(DataFolder, hash + Extension);
    }

    // ───── Helpers ────────────────────────────────────────────────────────

    private static bool IsFileUnchanged(string audioFilePath, AudioAnalysisCacheEntry entry)
    {
        try
        {
            var info = new FileInfo(audioFilePath);

            // Comparacion de tamanio: si difiere, el archivo cambio.
            if (info.Length != entry.FileSize) return false;

            // Comparacion de mtime con 1s de tolerancia (algunos filesystems
            // solo guardan precision de segundos).
            var delta = Math.Abs((info.LastWriteTimeUtc - entry.LastWriteTimeUtc).TotalSeconds);
            if (delta > 1.0) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeHash(string audioFilePath)
    {
        // Normalizar: path absoluto; lowercase en Windows (paths case-insensitive).
        var normalized = Path.GetFullPath(audioFilePath);
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToLowerInvariant();

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        return hex[..HashHexLength];
    }

    private static string BuildDataFolder()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "data");
    }
}