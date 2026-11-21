using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Persistencia de playlists en archivos .rmf (JSON).
///
/// El formato es autocontenido: toda la informacion de la playlist (pistas,
/// mixados, modo, notas) se serializa en un unico archivo .rmf.
/// -------------
public static class PlaylistStore
{
    public const string FileExtension = ".rmf";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── Save ──────────────────────────────────────────────────────────

    public static void Save(Playlist playlist, string path)
    {
        if (playlist is null) throw new ArgumentNullException(nameof(playlist));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            path += FileExtension;

        playlist.ModifiedAtUtc = DateTime.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(playlist, Options);
        File.WriteAllText(path, json);
    }

    // ───── Load ──────────────────────────────────────────────────────────

    public static Playlist? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var playlist = JsonSerializer.Deserialize<Playlist>(json, Options);

            if (playlist is null) return null;

            playlist.Tracks ??= new List<PlaylistTrack>();
            return playlist;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando .rmf: {ex.Message}");
            return null;
        }
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
            return doc.RootElement.TryGetProperty("Tracks", out _);
        }
        catch
        {
            return false;
        }
    }
}