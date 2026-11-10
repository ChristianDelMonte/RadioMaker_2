using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Persistencia de la botonera (.rcb / JSON).
///
/// Al cargar una botonera guardada con un tamano de grilla distinto al
/// actual (por ejemplo, una guardada con 5x10 y ahora el default es 4x6),
/// se aplica una MIGRACION automatica que preserva los botones con archivo
/// asignado, compactandolos en la nueva grilla y descartando los vacios
/// sobrantes.
/// </summary>
public static class CartBoardStore
{
    public const string FileExtension = ".rcb";

    /// <summary>Tamano de grilla actual por defecto (4 filas x 6 columnas = 24).</summary>
    public const int DefaultRows = 4;
    public const int DefaultColumns = 6;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── Save ──────────────────────────────────────────────────────────

    public static void Save(CartBoard board, string path)
    {
        if (board is null) throw new ArgumentNullException(nameof(board));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            path += FileExtension;

        board.ModifiedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(board, Options);
        File.WriteAllText(path, json);
    }

    // ───── Load ──────────────────────────────────────────────────────────

    public static CartBoard? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var board = JsonSerializer.Deserialize<CartBoard>(json, Options);
            if (board is null) return null;

            board.Buttons ??= new List<CartButton>();

            // Migracion automatica si la grilla guardada es distinta a la actual.
            MigrateIfNeeded(board);

            board.Normalize();
            return board;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando .rcb: {ex.Message}");
            return null;
        }
    }

    // ───── Migracion ─────────────────────────────────────────────────────

    /// <summary>
    /// Detecta si el tamano de grilla guardado es distinto al actual
    /// (DefaultRows x DefaultColumns) y, si es asi, aplica una migracion
    /// que conserva todos los botones con archivo, compactandolos en la
    /// nueva grilla.
    ///
    /// Reglas:
    ///   1. Se preservan TODOS los botones con archivo asignado, en su
    ///      orden original, empezando desde la posicion 0.
    ///   2. Los botones vacios que quedan dentro de la nueva capacidad se
    ///      rellenan al final.
    ///   3. Los botones vacios sobrantes se descartan.
    ///   4. Si la migracion provoca perdida de botones asignados, no se
    ///      aplica (para no destruir datos del usuario).
    /// </summary>
    private static void MigrateIfNeeded(CartBoard board)
    {
        // Solo migramos si el tamano cambio.
        if (board.Rows == DefaultRows && board.Columns == DefaultColumns)
            return;

        int newCapacity = DefaultRows * DefaultColumns;

        // Separar asignados y vacios.
        var assigned = board.Buttons.Where(b => b.HasFile).ToList();

        // Si hay mas botones asignados que la nueva capacidad, no migramos
        // (mejor conservar la grilla vieja que perder datos).
        if (assigned.Count > newCapacity)
            return;

        var empty = board.Buttons.Where(b => !b.HasFile).ToList();

        // Reconstruir la lista: primero los asignados, luego los vacios.
        var newList = new List<CartButton>();
        newList.AddRange(assigned);

        int emptySlots = newCapacity - newList.Count;
        for (int i = 0; i < emptySlots && i < empty.Count; i++)
        {
            newList.Add(empty[i]);
        }

        // Rellenar el resto con botones nuevos vacios.
        while (newList.Count < newCapacity)
            newList.Add(new CartButton());

        // Aplicar.
        board.Rows = DefaultRows;
        board.Columns = DefaultColumns;
        board.Buttons = newList;

        System.Diagnostics.Debug.WriteLine(
            $"CartBoard migrado a {DefaultRows}x{DefaultColumns}. " +
            $"{assigned.Count} boton(es) asignado(s) preservado(s).");
    }

    // ───── Path por defecto ──────────────────────────────────────────────

    /// <summary>
    /// Ruta de la botonera por defecto en el directorio de datos del usuario.
    /// Se usa cuando la app arranca sin un .rcb cargado explicitamente.
    /// </summary>
    public static string DefaultPath { get; } = BuildDefaultPath();

    /// <summary>
    /// Carga la botonera por defecto. Si no existe, crea una nueva
    /// (con el tamano actual por defecto) y la guarda en disco.
    /// </summary>
    public static CartBoard LoadOrCreateDefault()
    {
        var loaded = Load(DefaultPath);
        if (loaded is not null) return loaded;

        var fresh = new CartBoard
        {
            Rows = DefaultRows,
            Columns = DefaultColumns
        };
        fresh.Normalize();

        try { Save(fresh, DefaultPath); } catch { }

        return fresh;
    }

    // ───── Validation ────────────────────────────────────────────────────

    public static bool IsValidFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return false;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return root.TryGetProperty("Buttons", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildDefaultPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "cartboard" + FileExtension);
    }
}