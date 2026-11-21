using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Configuracion persistente del sistema de locución (RMVoice).
/// -------------
public sealed partial class LocutionSettings : ObservableObject
{
    /// -------------
    /// ID del bus de salida usado para las locuciones disparadas desde
    /// los botones del panel (hora / clima). No afecta a las locuciones
    /// que van dentro de una playlist (esas usan el bus de la playlist).
    /// -------------
    [ObservableProperty] private string _outputBusId = "primary";

    /// -------------Volumen de las locuciones disparadas desde los botones (0.0 a 1.0).</summary>
    [ObservableProperty] private double _buttonLocutionVolume = 0.9;

    /// -------------
    /// Si true, las locuciones disparadas desde los botones del panel
    /// bajan el volumen del audio que este sonando en el mismo bus
    /// mientras dura la locucion.
    /// -------------
    [ObservableProperty] private bool _buttonDuckEnabled = true;

    /// -------------
    /// Nivel al que se reduce el audio del bus durante una locucion del
    /// panel (0.0 a 1.0). 0.3 = el audio queda al 30%.
    /// -------------
    [ObservableProperty] private double _buttonDuckLevel = 0.3;

    /// -------------Si true, el clima se refresca automaticamente cada X minutos.</summary>
    [ObservableProperty] private bool _autoRefreshEnabled = true;

    /// -------------Intervalo de refresh automatico del clima, en minutos.</summary>
    [ObservableProperty] private int _weatherRefreshMinutes = 15;
}

public static class LocutionSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    public static LocutionSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<LocutionSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch { }
        return new LocutionSettings();
    }

    public static void Save(LocutionSettings settings)
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
        return Path.Combine(root, "RadioMaker", "locution.json");
    }
}