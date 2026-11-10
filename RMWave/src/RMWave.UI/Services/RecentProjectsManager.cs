using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "rmwave_recent.json");

    public static List<RecentProjectEntry> RecentList { get; private set; } = new();

    public static event Action? ListChanged;

    /// <summary>Agrega o actualiza un proyecto en la lista de recientes.</summary>
    public static void AddProject(string path, string name)
    {
        // Quitar si ya existe (para moverlo al inicio).
        RecentList.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        // Agregar al inicio.
        RecentList.Insert(0, new RecentProjectEntry
        {
            Path = path,
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
        RecentList.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
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
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json);
                if (list != null)
                    RecentList = list;
            }
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(RecentList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}