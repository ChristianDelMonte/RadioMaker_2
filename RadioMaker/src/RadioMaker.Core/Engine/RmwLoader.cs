using System;
using System.IO;
using System.Text.Json;
using RMWave.Core.Models;

namespace RadioMaker.Core.Engine;

/// -------------
/// Carga y cachea proyectos de RMWave (.rmw) desde disco.
///
/// Un .rmw es un archivo JSON con el modelo completo del proyecto:
/// pistas, clips, fades, marcadores, metadatos. NO es un archivo de audio.
/// -------------
internal static class RmwLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// -------------True si el path corresponde a un archivo .rmw.</summary>
    public static bool IsRmwFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return filePath.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase);
    }

    /// -------------
    /// Carga el proyecto desde disco. Devuelve null si el archivo no existe,
    /// el JSON es inválido, o el proyecto está vacío.
    /// -------------
    public static Project? Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[RmwLoader] Archivo no encontrado: {filePath}");
                return null;
            }

            var json = File.ReadAllText(filePath);
            var project = JsonSerializer.Deserialize<Project>(json, Options);

            if (project is null)
            {
                System.Diagnostics.Debug.WriteLine($"[RmwLoader] Deserialización null para '{filePath}'.");
                return null;
            }

            if (project.Tracks is null)
                project.Tracks = new System.Collections.Generic.List<ProjectTrack>();

            if (project.SampleRate <= 0) project.SampleRate = 44100;

            return project;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RmwLoader] Error cargando '{filePath}': {ex.Message}");
            return null;
        }
    }

    /// -------------Duración del proyecto sin inicializar el motor de audio.</summary>
    public static double GetDuration(string filePath)
    {
        var project = Load(filePath);
        return project?.Duration ?? 0;
    }
}