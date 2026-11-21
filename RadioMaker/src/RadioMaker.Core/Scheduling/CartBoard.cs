using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// La botonera completa (Modulo 3B).
/// Contiene una grilla de botones configurables y se persiste en un
/// archivo .rcb (JSON).
/// -------------
public sealed class CartBoard
{
    /// -------------Formato del archivo, para versiones futuras.</summary>
    public int Version { get; set; } = 1;

    /// -------------Nombre de la botonera.</summary>
    public string Name { get; set; } = "Botonera";

    /// -------------Cantidad de filas de la grilla. Por defecto 4.</summary>
    public int Rows { get; set; } = 4;

    /// -------------Cantidad de columnas de la grilla. Por defecto 6.</summary>
    public int Columns { get; set; } = 6;

    /// -------------
    /// Lista plana de botones. El orden es row-major: indice = fila * Columns + columna.
    /// La lista puede tener menos elementos que Rows * Columns (los faltantes
    /// se consideran botones vacios).
    /// -------------
    public List<CartButton> Buttons { get; set; } = new();

    /// -------------
    /// ID del bus de salida asignado a esta botonera. Se persiste en el
    /// .rcb para que la configuracion viaje con el archivo.
    ///
    /// Reglas:
    ///   - Vacio = usar el bus del modulo Cart (default).
    ///   - Si al abrir en otra PC el bus no existe, se cae al bus del modulo.
    ///   - Solo el usuario puede cambiarlo desde el combo de la toolbar.
    /// -------------
    public string OutputBusId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    // ───── Helpers ───────────────────────────────────────────────────────

    /// -------------Capacidad total de la grilla.</summary>
    [JsonIgnore]
    public int Capacity => Rows * Columns;

    /// -------------
    /// Normaliza la lista de botones para que tenga exactamente Capacity
    /// elementos (rellena con botones vacios si faltan, o trunca si sobran).
    /// -------------
    public void Normalize()
    {
        int cap = Capacity;

        while (Buttons.Count < cap)
            Buttons.Add(new CartButton());

        if (Buttons.Count > cap)
            Buttons.RemoveRange(cap, Buttons.Count - cap);
    }

    /// -------------
    /// Redimensiona la grilla preservando los botones existentes hasta el
    /// maximo que quepa en la nueva grilla. Los botones que sobresalgan del
    /// nuevo tamano se descartan.
    /// -------------
    public void SetGridSize(int rows, int columns)
    {
        rows = Math.Max(1, rows);
        columns = Math.Max(1, columns);

        Rows = rows;
        Columns = columns;
        Normalize();
    }
}