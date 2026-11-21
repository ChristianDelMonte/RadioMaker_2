namespace RadioMaker.UI.Services;

/// -------------
/// Servicio central de carpetas favoritas del panel derecho.
///
/// Responsabilidades:
///   - Cargar y persistir la lista en disco.
///   - Exponer operaciones atómicas: Add, Remove, Rename.
///   - Notificar cambios con <see cref="Changed"/> para que la UI se actualice.
///   - Indicar si una carpeta ya es favorita (por ruta absoluta).
///
/// Los favoritos son únicos por ruta: no se puede agregar la misma carpeta
/// dos veces. La comparación es case-insensitive en Windows y case-sensitive
/// en Linux/macOS, delegando en PathComparison (que ya maneja esa diferencia).
/// -------------
public sealed class FavoriteFolderRegistry
{
    private readonly object _gate = new();
    private readonly List<FavoriteFolderEntry> _entries = new();

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// -------------Se dispara cuando la lista cambia (agregar, quitar, renombrar).</summary>
    public event EventHandler? Changed;

    public FavoriteFolderRegistry()
    {
        var loaded = FavoriteFolderStore.Load();
        _entries.AddRange(loaded);
    }

    // ───── Consultas ─────────────────────────────────────────────────────

    /// -------------Snapshot de todos los favoritos.</summary>
    public IReadOnlyList<FavoriteFolderEntry> GetAll()
    {
        lock (_gate) return _entries.ToList();
    }

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// -------------Devuelve el favorito por Id o null.</summary>
    public FavoriteFolderEntry? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate) return _entries.FirstOrDefault(e => e.Id == id);
    }

    /// -------------True si la ruta indicada ya está en favoritos.</summary>
    public bool IsFavorite(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;

        var normalized = Normalize(folderPath);
        if (normalized is null) return false;

        lock (_gate)
        {
            return _entries.Any(e => PathsEqual(e.Path, normalized));
        }
    }

    // ───── Modificaciones ────────────────────────────────────────────────

    /// -------------
    /// Agrega una carpeta a favoritos. Devuelve la entry creada o null
    /// si la ruta ya existía o es inválida.
    /// -------------
    /// <param name="folderPath">Ruta absoluta de la carpeta.</param>
    /// <param name="customName">
    /// Nombre personalizado opcional. Si es null o vacío, se usa el nombre
    /// de la carpeta.
    /// </param>
    public FavoriteFolderEntry? Add(string folderPath, string? customName = null)
    {
        var normalized = Normalize(folderPath);
        if (normalized is null) return null;

        lock (_gate)
        {
            if (_entries.Any(e => PathsEqual(e.Path, normalized)))
                return null;

            var entry = new FavoriteFolderEntry
            {
                Path = normalized,
                CustomName = (customName ?? string.Empty).Trim(),
                AddedAtUtc = DateTime.UtcNow
            };

            _entries.Add(entry);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return GetById(_entries[^1].Id);
    }

    /// -------------
    /// Quita una carpeta de favoritos por ruta. Devuelve true si algo se eliminó.
    /// -------------
    public bool Remove(string folderPath)
    {
        var normalized = Normalize(folderPath);
        if (normalized is null) return false;

        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => PathsEqual(e.Path, normalized)) > 0;
            if (removed) Save();
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// -------------Quita un favorito por Id. Devuelve true si algo se eliminó.</summary>
    public bool RemoveById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => e.Id == id) > 0;
            if (removed) Save();
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// -------------
    /// Renombra un favorito. Si <paramref name="newName"/> es null o vacío,
    /// el favorito vuelve a usar el nombre por defecto de la carpeta.
    /// Devuelve true si algo cambió.
    /// -------------
    public bool Rename(string id, string? newName)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        bool changed = false;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return false;

            var trimmed = (newName ?? string.Empty).Trim();
            if (!string.Equals(entry.CustomName, trimmed, StringComparison.Ordinal))
            {
                entry.CustomName = trimmed;
                changed = true;
                Save();
            }
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    /// -------------
    /// Normaliza una ruta a forma absoluta. Devuelve null si es inválida.
    /// -------------
    private static string? Normalize(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        try
        {
            return Path.GetFullPath(folderPath);
        }
        catch
        {
            return null;
        }
    }

    /// -------------Compara dos rutas según la plataforma.</summary>
    private static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

        // Trim separadores finales para evitar falsos negativos.
        a = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        b = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(a, b, PathComparison);
    }

    private void Save()
    {
        // Se llama dentro del lock, ya tiene snapshot consistente.
        FavoriteFolderStore.Save(_entries);
    }
}