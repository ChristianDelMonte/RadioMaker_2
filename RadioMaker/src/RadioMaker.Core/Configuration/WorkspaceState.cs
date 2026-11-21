using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Configuration;

/// -------------
/// Tipo de pestaña guardada en el workspace.
/// Espejo de TabKind de la UI, definido aqui para que el archivo
/// workspace.json sea autocontenido.
/// -------------
public enum WorkspaceTabKind
{
    Scheduled = 0,
    Tanda = 1,
    Playlist = 2,
    Notes = 3,
    Cart = 4,
    FavoriteFolder = 5,
    Encadenadores = 6
}

/// -------------
/// Zona de la ventana donde vive una pestaña persistida.
/// El valor por defecto (Left = 0) garantiza compatibilidad con
/// workspaces guardados antes de introducir el campo.
/// -------------
public enum WorkspaceTabZone
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// -------------
/// Entrada individual de una pestaña guardada.
/// -------------
public sealed class WorkspaceTabEntry
{
    public WorkspaceTabKind Kind { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// -------------Solo para pestañas Encadenadores: indica si es la activa.</summary>
    public bool IsActive { get; set; }

    /// -------------
    /// Solo para pestañas de tipo Notes. Identifica el archivo de draft
    /// asociado en %APPDATA%/RmsAudioPlayer/notes/.
    /// -------------
    public string DraftId { get; set; } = string.Empty;

    /// -------------
    /// Zona donde vivia la pestaña. Default = Left para no romper
    /// workspaces viejos que no tenian este campo.
    /// -------------
    public WorkspaceTabZone Zone { get; set; } = WorkspaceTabZone.Left;

    /// -------------
    /// Solo para pestañas de tipo FavoriteFolder. Identifica el favorito
    /// del FavoriteFolderRegistry que hay que abrir.
    /// -------------
    public string FavoriteId { get; set; } = string.Empty;
}

/// -------------
/// Estado completo del workspace: lista de pestañas + indice activo.
/// -------------
public sealed class WorkspaceState
{
    public int Version { get; set; } = 1;
    public List<WorkspaceTabEntry> Tabs { get; set; } = new();

    /// -------------Indice activo de la zona izquierda (compat con version vieja).</summary>
    public int ActiveTabIndex { get; set; } = 0;

    /// -------------Indice activo de la zona izquierda (nuevo).</summary>
    public int ActiveTabIndexLeft { get; set; } = 0;

    /// -------------Indice activo de la zona central (nuevo).</summary>
    public int ActiveTabIndexCenter { get; set; } = 0;

    /// -------------Indice activo de la zona derecha (nuevo).</summary>
    public int ActiveTabIndexRight { get; set; } = 0;

    /// -------------
    /// Ruta del ultimo archivo .rcb abierto como botonera principal
    /// (la pestaña fija del centro). Al arrancar, si existe, se carga.
    /// Vacio = usar la ruta por defecto (cartboard.rcb en AppData).
    /// -------------
    public string PrimaryCartPath { get; set; } = string.Empty;
}

/// -------------
/// Persistencia del workspace en %APPDATA%/RmsAudioPlayer/workspace.json.
/// -------------
public static class WorkspaceStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    /// -------------Carga el workspace. Devuelve null si no existe o esta corrupto.</summary>
    public static WorkspaceState? Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json, Options);
            if (state is null) return null;
            state.Tabs ??= new List<WorkspaceTabEntry>();
            return state;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WorkspaceState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    /// -------------Borra el workspace (util para "Empezar de cero").</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "workspace.json");
    }
}
