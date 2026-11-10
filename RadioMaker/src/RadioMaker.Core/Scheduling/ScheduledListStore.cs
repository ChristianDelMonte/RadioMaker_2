using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Persistencia de listas de programados en archivos .rms (JSON).
///
/// Formato: JSON legible con todas las propiedades del modelo ScheduledList.
/// Los enums de prioridad se serializan como string ("Low", "High") para
/// que el archivo sea legible a mano si el usuario quiere editarlo.
/// </summary>
public static class ScheduledListStore
{
    public const string FileExtension = ".rms";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── Save ──────────────────────────────────────────────────────────

    /// <summary>Guarda la lista en el path indicado. Anade la extension si falta.</summary>
    public static void Save(ScheduledList list, string path)
    {
        if (list is null) throw new ArgumentNullException(nameof(list));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            path += FileExtension;

        list.ModifiedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(list, Options);
        File.WriteAllText(path, json);
    }

    // ───── Load ──────────────────────────────────────────────────────────

    /// <summary>Carga una lista desde un archivo .rms. Devuelve null si falla.</summary>
    public static ScheduledList? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<ScheduledList>(json, Options);

            if (list is null) return null;

            // Garantizar que las colecciones no sean null tras deserializar.
            list.Items ??= new List<ScheduledItem>();
            foreach (var item in list.Items)
            {
                item.Entries ??= new List<ScheduleEntry>();
                foreach (var entry in item.Entries)
                {
                    entry.Days ??= new List<DayOfWeek>();
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando .rms: {ex.Message}");
            return null;
        }
    }

    // ───── Validation ────────────────────────────────────────────────────

    /// <summary>Valida que un archivo sea una lista .rms legible.</summary>
    public static bool IsValidFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return false;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Chequear que exista la propiedad "Items" (es lo minimo indispensable).
            return root.TryGetProperty("Items", out _);
        }
        catch
        {
            return false;
        }
    }
}