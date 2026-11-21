using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Configuracion persistente del panel de analisis de silencios.
/// Incluye el flag de auto-trim que hace que al reproducir se salte el
/// silencio inicial y se termine antes del silencio final.
/// -------------
public sealed partial class SilencePanelSettings : ObservableObject
{
    /// -------------Si true, al reproducir se aplica el recorte automatico.</summary>
    [ObservableProperty] private bool _autoTrimEnabled = false;

    /// -------------Umbral en dB por debajo del cual se considera silencio.</summary>
    [ObservableProperty] private double _thresholdDb = -50.0;

    /// -------------Tamano de bloque en milisegundos para el analisis.</summary>
    [ObservableProperty] private int _blockMs = 50;

    /// -------------Duracion minima (ms) para considerar un silencio interno.</summary>
    [ObservableProperty] private int _minSilenceMs = 300;

    /// -------------
    /// Si true, al cargar un archivo se analiza automaticamente
    /// (util cuando AutoTrimEnabled esta activo).
    /// -------------
    [ObservableProperty] private bool _autoAnalyzeOnLoad = true;
}

public static class SilencePanelSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    public static SilencePanelSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<SilencePanelSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch { }
        return new SilencePanelSettings();
    }

    public static void Save(SilencePanelSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "silence.json");
    }
}