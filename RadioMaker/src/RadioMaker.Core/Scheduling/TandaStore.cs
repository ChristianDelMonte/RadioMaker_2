using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Persistencia de tandas en archivos .tnd (JSON).
///
/// Formato: JSON legible con las propiedades del modelo Tanda.
/// El archivo .tnd puede:
///   - Guardarse/abrirse independientemente desde el menu Tanda.
///   - Incrustarse dentro de un item programado en un .rms.
/// </summary>
public static class TandaStore
{
    public const string FileExtension = ".tnd";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── Save ──────────────────────────────────────────────────────────

    /// <summary>Guarda la tanda en el path indicado. Anade la extension si falta.</summary>
    public static void Save(Tanda tanda, string path)
    {
        if (tanda is null) throw new ArgumentNullException(nameof(tanda));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            path += FileExtension;

        tanda.ModifiedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(tanda, Options);
        File.WriteAllText(path, json);
    }

    // ───── Load ──────────────────────────────────────────────────────────

    /// <summary>Carga una tanda desde un archivo .tnd. Devuelve null si falla.</summary>
    public static Tanda? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var tanda = JsonSerializer.Deserialize<Tanda>(json, Options);

            if (tanda is null) return null;

            // Garantizar que Tracks no sea null.
            tanda.Tracks ??= new List<TandaTrack>();

            return tanda;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando .tnd: {ex.Message}");
            return null;
        }
    }

    // ───── Serialization helpers ─────────────────────────────────────────

    /// <summary>Serializa una tanda a string JSON. Util para incrustarla en un .rms.</summary>
    public static string SerializeToString(Tanda tanda)
    {
        if (tanda is null) throw new ArgumentNullException(nameof(tanda));
        return JsonSerializer.Serialize(tanda, Options);
    }

    /// <summary>Deserializa una tanda desde un string JSON.</summary>
    public static Tanda? DeserializeFromString(string json)
    {
        try
        {
            var tanda = JsonSerializer.Deserialize<Tanda>(json, Options);
            if (tanda is null) return null;
            tanda.Tracks ??= new List<TandaTrack>();
            return tanda;
        }
        catch
        {
            return null;
        }
    }

    // ───── Validation ────────────────────────────────────────────────────

    /// <summary>Valida que un archivo sea una tanda .tnd legible.</summary>
    public static bool IsValidFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return false;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Una tanda debe tener la propiedad "Tracks" (puede estar vacia).
            return root.TryGetProperty("Tracks", out _);
        }
        catch
        {
            return false;
        }
    }
}