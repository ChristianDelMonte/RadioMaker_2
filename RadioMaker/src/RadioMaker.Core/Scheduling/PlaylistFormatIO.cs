using System.Text;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Importacion / exportacion de playlists en formatos M3U / M3U8 y PLS.
///
/// M3U: formato de texto, un path por linea, precedido opcionalmente por
///      #EXTINF:duracion,artista - titulo. M3U8 es la variante UTF-8.
///
/// PLS: formato INI con seccion [playlist] y campos FileN, TitleN, LengthN.
///      Termina con NumberOfEntries=N y Version=2.
///
/// Al importar se resuelven rutas relativas contra el directorio del archivo.
/// Al exportar se escriben rutas ABSOLUTAS para maxima compatibilidad.
///
/// Los formatos M3U/PLS no soportan mixados ni volumenes relativos: esos
/// datos se preservan al trabajar con .rmf pero se pierden al exportar a
/// M3U/PLS. Al reimportar un M3U/PLS se crean pistas basicas sin mixado.
/// </summary>
public static class PlaylistFormatIO
{
    public const string M3uExtension = ".m3u";
    public const string M3u8Extension = ".m3u8";
    public const string PlsExtension = ".pls";

    // ═════════════════════════════════════════════════════════════════════
    // EXPORT
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Exporta la playlist como archivo M3U / M3U8.</summary>
    public static void ExportM3U(Playlist playlist, string path)
    {
        if (playlist is null) throw new ArgumentNullException(nameof(playlist));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var track in playlist.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.FilePath)) continue;

            // Calculamos duracion desconocida (-1) porque no abrimos el archivo.
            var title = string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileNameWithoutExtension(track.FilePath)
                : track.Title;

            sb.AppendLine($"#EXTINF:-1,{title}");
            sb.AppendLine(track.FilePath);
        }

        // M3U8 siempre UTF-8 sin BOM; M3U clasico es ANSI pero UTF-8 funciona bien.
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Exporta la playlist como archivo PLS.</summary>
    public static void ExportPLS(Playlist playlist, string path)
    {
        if (playlist is null) throw new ArgumentNullException(nameof(playlist));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path vacio", nameof(path));

        var sb = new StringBuilder();
        sb.AppendLine("[playlist]");

        int index = 0;
        foreach (var track in playlist.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.FilePath)) continue;
            index++;

            var title = string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileNameWithoutExtension(track.FilePath)
                : track.Title;

            sb.AppendLine($"File{index}={track.FilePath}");
            sb.AppendLine($"Title{index}={title}");
            sb.AppendLine($"Length{index}=-1");
        }

        sb.AppendLine($"NumberOfEntries={index}");
        sb.AppendLine("Version=2");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ═════════════════════════════════════════════════════════════════════
    // IMPORT
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Detecta el formato por extension y lo importa.</summary>
    public static Playlist? ImportAuto(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            M3uExtension or M3u8Extension => ImportM3U(path),
            PlsExtension => ImportPLS(path),
            _ => ImportM3U(path) // fallback
        };
    }

    /// <summary>Importa un archivo M3U / M3U8.</summary>
    public static Playlist? ImportM3U(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var playlist = new Playlist
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Tracks = new List<PlaylistTrack>()
            };

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;

            string? pendingTitle = null;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Comentario #EXTINF:duracion,artista - titulo
                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = line.IndexOf(',');
                    if (comma >= 0 && comma < line.Length - 1)
                    {
                        var meta = line[(comma + 1)..].Trim();
                        // Preferimos solo el titulo (si hay "Artista - Titulo", tomamos todo).
                        pendingTitle = !string.IsNullOrWhiteSpace(meta) ? meta : null;
                    }
                    continue;
                }

                // Otros comentarios
                if (line.StartsWith("#")) continue;

                // Es un path (absoluto o relativo)
                var resolved = ResolvePath(line, baseDir);
                if (string.IsNullOrEmpty(resolved)) continue;

                playlist.Tracks.Add(new PlaylistTrack
                {
                    FilePath = resolved,
                    Title = pendingTitle ?? Path.GetFileNameWithoutExtension(resolved),
                    IsEnabled = true
                });

                pendingTitle = null;
            }

            return playlist;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importando M3U: {ex.Message}");
            return null;
        }
    }

    /// <summary>Importa un archivo PLS.</summary>
    public static Playlist? ImportPLS(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var playlist = new Playlist
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Tracks = new List<PlaylistTrack>()
            };

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;

            // Recolectamos los FileN y TitleN por indice y luego los ordenamos.
            var files = new Dictionary<int, string>();
            var titles = new Dictionary<int, string>();

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("[") || line.StartsWith(";")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(key[4..], out int fileIndex))
                {
                    files[fileIndex] = value;
                }
                else if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(key[5..], out int titleIndex))
                {
                    titles[titleIndex] = value;
                }
            }

            foreach (var kvp in files.OrderBy(k => k.Key))
            {
                var resolved = ResolvePath(kvp.Value, baseDir);
                if (string.IsNullOrEmpty(resolved)) continue;

                var title = titles.TryGetValue(kvp.Key, out var t) && !string.IsNullOrWhiteSpace(t)
                    ? t
                    : Path.GetFileNameWithoutExtension(resolved);

                playlist.Tracks.Add(new PlaylistTrack
                {
                    FilePath = resolved,
                    Title = title,
                    IsEnabled = true
                });
            }

            return playlist;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importando PLS: {ex.Message}");
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Si la ruta es absoluta la devuelve tal cual. Si es relativa la
    /// resuelve contra <paramref name="baseDir"/>.
    /// </summary>
    private static string ResolvePath(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            if (Path.IsPathRooted(path))
                return path;

            return Path.GetFullPath(Path.Combine(baseDir, path));
        }
        catch
        {
            return path;
        }
    }
}