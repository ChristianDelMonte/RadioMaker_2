using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.UI.Services;

/// <summary>
/// Persistencia del bus de salida seleccionado en el panel derecho:
///   - Un bus para el explorador de archivos (pestaña fija).
///   - Un bus por cada favorito abierto como pestaña (indexado por FavoriteId).
///
/// Guarda en %APPDATA%/RmsAudioPlayer/right-panel-buses.json.
/// </summary>
public static class RightPanelBusStore
{
    private static readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    /// <summary>Bus seleccionado para el explorador de archivos (Id o vacío).</summary>
    public static string GetExplorerBusId()
    {
        lock (_gate)
        {
            return LoadInternal()?.ExplorerBusId ?? string.Empty;
        }
    }

    public static void SetExplorerBusId(string busId)
    {
        if (string.IsNullOrEmpty(busId)) return;

        lock (_gate)
        {
            var state = LoadInternal() ?? new RightPanelBusState();
            state.ExplorerBusId = busId;
            SaveInternal(state);
        }
    }

    /// <summary>Bus seleccionado para un favorito concreto (Id o vacío).</summary>
    public static string GetFavoriteBusId(string favoriteId)
    {
        if (string.IsNullOrEmpty(favoriteId)) return string.Empty;

        lock (_gate)
        {
            var state = LoadInternal();
            if (state is null) return string.Empty;
            return state.FavoriteBusIds.TryGetValue(favoriteId, out var id) ? id : string.Empty;
        }
    }

    public static void SetFavoriteBusId(string favoriteId, string busId)
    {
        if (string.IsNullOrEmpty(favoriteId)) return;
        if (string.IsNullOrEmpty(busId)) return;

        lock (_gate)
        {
            var state = LoadInternal() ?? new RightPanelBusState();
            state.FavoriteBusIds[favoriteId] = busId;
            SaveInternal(state);
        }
    }

    /// <summary>Limpia el bus guardado de un favorito (por ejemplo si se quita de favoritos).</summary>
    public static void RemoveFavoriteBusId(string favoriteId)
    {
        if (string.IsNullOrEmpty(favoriteId)) return;

        lock (_gate)
        {
            var state = LoadInternal();
            if (state is null) return;
            if (state.FavoriteBusIds.Remove(favoriteId))
                SaveInternal(state);
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private static RightPanelBusState? LoadInternal()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<RightPanelBusState>(json, Options);
        }
        catch { return null; }
    }

    private static void SaveInternal(RightPanelBusState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "right-panel-buses.json");
    }

    private sealed class RightPanelBusState
    {
        public int Version { get; set; } = 1;
        public string ExplorerBusId { get; set; } = string.Empty;
        public Dictionary<string, string> FavoriteBusIds { get; set; } = new();
    }
}