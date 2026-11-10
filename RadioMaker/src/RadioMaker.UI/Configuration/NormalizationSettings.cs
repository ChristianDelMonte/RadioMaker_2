using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.Configuration;

/// <summary>
/// Configuracion persistente del panel de normalizacion de niveles.
/// </summary>
public sealed partial class NormalizationSettings : ObservableObject
{
    /// <summary>Si true, la normalizacion se aplica al reproducir.</summary>
    [ObservableProperty] private bool _isEnabled = false;

    /// <summary>Estrategia: Peak (evita clipping) o Rms (empareja sonoridad).</summary>
    [ObservableProperty] private NormalizationMode _mode = NormalizationMode.Peak;

    /// <summary>Target en dBFS. Tipico: -1 dB para Peak, -16 dB para RMS.</summary>
    [ObservableProperty] private double _targetDb = -1.0;

    /// <summary>
    /// Si true, al cargar un archivo se analiza automaticamente.
    /// Recomendado cuando IsEnabled esta activo.
    /// </summary>
    [ObservableProperty] private bool _autoAnalyzeOnLoad = true;
}

public static class NormalizationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static NormalizationSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<NormalizationSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch { }
        return new NormalizationSettings();
    }

    public static void Save(NormalizationSettings settings)
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
        return Path.Combine(root, "RadioMaker", "normalization.json");
    }
}