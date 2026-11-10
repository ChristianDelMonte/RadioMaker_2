namespace RadioMaker.UI.Services;

/// <summary>
/// Entrada de una carpeta favorita en el panel derecho.
///
/// El campo <see cref="CustomName"/> es opcional: si el usuario le asigna
/// un nombre, se usa ese; si está vacío, se usa el nombre de la carpeta.
/// </summary>
public sealed class FavoriteFolderEntry
{
    /// <summary>Identificador único, estable.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Ruta absoluta de la carpeta.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Nombre personalizado. Vacío = usar el nombre de la carpeta.</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>Fecha de alta (UTC).</summary>
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Nombre a mostrar en el menú "Nueva pestaña" y en el título de la
    /// pestaña cuando se abre este favorito.
    ///
    /// Regla: si hay CustomName, se usa; si no, se usa el último segmento
    /// del Path. Si el path está vacío, cae a "(sin nombre)".
    /// </summary>
    public string EffectiveName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomName))
                return CustomName;

            if (string.IsNullOrWhiteSpace(Path))
                return "(sin nombre)";

            // Sacar separadores finales antes de calcular el nombre.
            var trimmed = Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);

            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }
    }

    /// <summary>
    /// True si la carpeta referenciada existe en disco ahora mismo.
    /// Se evalúa on-demand, no se persiste.
    /// </summary>
    public bool ExistsNow
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Path) && Directory.Exists(Path);
            }
            catch
            {
                return false;
            }
        }
    }
}