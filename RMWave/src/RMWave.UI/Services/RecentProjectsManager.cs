using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace RMWave.UI;

public class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

public static class RecentProjectsManager
{
    private const int MaxRecent = 8;

    private static string FilePath
    {
        get
        {
            var dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(dataRoot, "RadioMaker", "RMWave", "rmwave_recent.json");
        }
    }

    private static string LegacyFilePath => Path.Combine(AppContext.BaseDirectory, "rmwave_recent.json");

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }

    public static List<RecentProjectEntry> RecentList { get; private set; } = new();

    public static event Action? ListChanged;

    /// <summary>Agrega o actualiza un proyecto en la lista de recientes.</summary>
    public static void AddProject(string path, string name)
    {
        // Quitar si ya existe (para moverlo al inicio).
        var normalizedPath = NormalizePath(path);
        RecentList.RemoveAll(e => !string.IsNullOrWhiteSpace(e.Path) &&
            NormalizePath(e.Path).Equals(normalizedPath, PathComparison));

        // Agregar al inicio.
        RecentList.Insert(0, new RecentProjectEntry
        {
            Path = normalizedPath,
            Name = name,
            LastOpened = DateTime.Now
        });

        // Limitar a MaxRecent.
        if (RecentList.Count > MaxRecent)
            RecentList.RemoveRange(MaxRecent, RecentList.Count - MaxRecent);

        Save();
        ListChanged?.Invoke();
    }

    /// <summary>Quita un proyecto de la lista.</summary>
    public static void RemoveProject(string path)
    {
        var normalizedPath = NormalizePath(path);
        RecentList.RemoveAll(e => !string.IsNullOrWhiteSpace(e.Path) &&
            NormalizePath(e.Path).Equals(normalizedPath, PathComparison));
        Save();
        ListChanged?.Invoke();
    }

    /// <summary>Limpia toda la lista.</summary>
    public static void Clear()
    {
        RecentList.Clear();
        Save();
        ListChanged?.Invoke();
    }

    public static void Load()
    {
        try
        {
            var sourcePath = File.Exists(FilePath)
                ? FilePath
                : LegacyFilePath;
            if (File.Exists(sourcePath))
            {
                var json = File.ReadAllText(sourcePath);
                var list = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json);
                if (list != null)
                    RecentList = list
                        .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path))
                        .Select(e =>
                        {
                            e.Path = NormalizePath(e.Path);
                            return e;
                        })
                        .Take(MaxRecent)
                        .ToList();

                if (!string.Equals(sourcePath, FilePath, PathComparison))
                    Save();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RMWave RecentProjects] No se pudieron cargar recientes: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(RecentList, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RMWave RecentProjects] No se pudieron guardar recientes en '{FilePath}': {ex.Message}");
        }
    }
}
