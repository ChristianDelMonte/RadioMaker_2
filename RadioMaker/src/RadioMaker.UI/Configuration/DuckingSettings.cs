using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Configuracion persistente del panel de ducking por microfono.
/// -------------
public sealed partial class DuckingSettings : ObservableObject
{
    [ObservableProperty] private bool _isEnabled = false;
    [ObservableProperty] private int _micDeviceIndex = -1;
    [ObservableProperty] private double _thresholdDb = -40.0;
    [ObservableProperty] private double _reductionDb = 15.0;
    [ObservableProperty] private int _holdMs = 400;
    [ObservableProperty] private int _attackMs = 80;
    [ObservableProperty] private int _releaseMs = 600;

    /// -------------Convierte esta configuracion al modelo del AudioCore.</summary>
    public DuckingConfig ToConfig() => new()
    {
        IsEnabled = IsEnabled,
        MicDeviceIndex = MicDeviceIndex,
        ThresholdDb = ThresholdDb,
        ReductionDb = ReductionDb,
        HoldMs = HoldMs,
        AttackMs = AttackMs,
        ReleaseMs = ReleaseMs,
    };
}

public static class DuckingSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static DuckingSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<DuckingSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch (Exception ex) { AppLogger.Warning("SETTINGS DUCKING", "No se pudo leer la configuración guardada.", ex); }
        return new DuckingSettings();
    }

    public static void Save(DuckingSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) { AppLogger.Error("SETTINGS DUCKING", "No se pudo guardar la configuración.", ex); }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "ducking.json");
    }
}
