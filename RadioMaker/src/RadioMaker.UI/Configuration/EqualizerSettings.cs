using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Configuracion del ecualizador de 10 bandas.
/// Se persiste en JSON en el directorio del usuario.
/// Incluye presets personalizados definidos por el usuario.
/// -------------
public sealed partial class EqualizerSettings : ObservableObject
{
    public const int BandCount = 10;

    public static readonly float[] Frequencies =
        { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

    public static readonly string[] FrequencyLabels =
        { "31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

    [ObservableProperty] private bool _isEnabled = false;
    [ObservableProperty] private string _presetName = "Flat";
    [ObservableProperty] private double[] _gains = new double[BandCount];

    /// -------------
    /// Presets personalizados definidos por el usuario.
    /// Clave = nombre del preset, valor = ganancias de las 10 bandas.
    /// Se persisten en un archivo JSON aparte (equalizer-presets.json).
    /// -------------
    [ObservableProperty] private Dictionary<string, double[]> _customPresets = new();

    public double MinGain => -12.0;
    public double MaxGain => +12.0;

    /// -------------Se dispara cuando cambia cualquier ganancia individual.</summary>
    public event EventHandler<int>? BandChanged;

    /// -------------Se dispara cuando cambian todas las ganancias a la vez (preset).</summary>
    public event EventHandler? AllBandsChanged;

    /// -------------Se dispara cuando se agrega o elimina un preset personalizado.</summary>
    public event EventHandler? CustomPresetsChanged;

    public void SetBandGain(int band, double gain)
    {
        if (band < 0 || band >= BandCount) return;
        double clamped = Math.Clamp(gain, MinGain, MaxGain);
        if (Math.Abs(Gains[band] - clamped) < 0.001) return;

        Gains[band] = clamped;
        PresetName = "Personalizado";
        BandChanged?.Invoke(this, band);
    }

    public void ApplyPreset(string name)
    {
        if (!TryGetPresetValues(name, out var values)) return;
        if (values.Length != BandCount) return;

        for (int i = 0; i < BandCount; i++)
            Gains[i] = Math.Clamp(values[i], MinGain, MaxGain);

        PresetName = name;
        AllBandsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToFlat() => ApplyPreset("Flat");

    /// -------------Agrega o reemplaza un preset personalizado.</summary>
    public void SaveCustomPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        // Clonar el array para no compartir referencia con Gains.
        var snapshot = (double[])Gains.Clone();
        CustomPresets[name] = snapshot;

        PresetName = name;
        CustomPresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// -------------Elimina un preset personalizado.</summary>
    public bool DeleteCustomPreset(string name)
    {
        if (!CustomPresets.ContainsKey(name)) return false;

        CustomPresets.Remove(name);
        CustomPresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// -------------Devuelve true si el nombre corresponde a un preset personalizado.</summary>
    public bool IsCustomPreset(string name) => CustomPresets.ContainsKey(name);

    private bool TryGetPresetValues(string name, out double[] values)
    {
        if (CustomPresets.TryGetValue(name, out values!)) return true;
        if (Presets.TryGetValue(name, out values!)) return true;
        values = Array.Empty<double>();
        return false;
    }

    // ───── Presets de fabrica (no eliminables) ───────────────────────────

    public static readonly IReadOnlyDictionary<string, double[]> Presets = new Dictionary<string, double[]>
    {
        { "Flat",         new double[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 } },
        { "Rock",         new double[] {  5,  4,  3,  1, -1, -1,  1,  3,  4,  5 } },
        { "Pop",          new double[] { -1,  0,  2,  4,  4,  2,  0, -1, -1, -2 } },
        { "Jazz",         new double[] {  4,  3,  1,  2, -1, -1,  0,  1,  3,  4 } },
        { "Classical",    new double[] {  4,  3,  2,  1, -1, -1,  0,  2,  3,  4 } },
        { "Vocal",        new double[] { -2, -3, -3,  1,  4,  4,  3,  1,  0, -1 } },
        { "Bass Boost",   new double[] {  7,  6,  5,  3,  1,  0,  0,  0,  0,  0 } },
        { "Treble Boost", new double[] {  0,  0,  0,  0,  0,  1,  3,  5,  6,  7 } },
        { "Loudness",     new double[] {  6,  4,  0, -1, -3, -1,  0,  4,  6,  6 } },
        { "Podcast",      new double[] { -4, -3, -1,  2,  3,  3,  2,  1,  0, -1 } },
    };
}

/// -------------
/// Persistencia de la configuracion del ecualizador:
///   • Estado actual (bandas, habilitado, preset activo) → equalizer.json
///   • Presets personalizados                            → equalizer-presets.json
/// -------------
public static class EqualizerSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ConfigPath { get; } =
        BuildConfigPath("equalizer.json");

    public static string PresetsPath { get; } =
        BuildConfigPath("equalizer-presets.json");

    public static EqualizerSettings Load()
    {
        var settings = new EqualizerSettings();

        // 1. Cargar estado actual.
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<EqualizerSettings>(json, Options);
                if (loaded is not null && loaded.Gains.Length == EqualizerSettings.BandCount)
                {
                    settings.IsEnabled = loaded.IsEnabled;
                    settings.PresetName = loaded.PresetName;
                    settings.Gains = loaded.Gains;
                }
            }
        }
        catch { }

        // 2. Cargar presets personalizados.
        try
        {
            if (File.Exists(PresetsPath))
            {
                var json = File.ReadAllText(PresetsPath);
                var presets = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json, Options);
                if (presets is not null)
                {
                    settings.CustomPresets = presets
                        .Where(kv => kv.Value.Length == EqualizerSettings.BandCount)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            }
        }
        catch { }

        return settings;
    }

    public static void Save(EqualizerSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Guardar el estado actual (sin los presets custom, que van aparte).
            var state = new
            {
                settings.IsEnabled,
                settings.PresetName,
                settings.Gains
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));

            // Guardar los presets personalizados.
            File.WriteAllText(PresetsPath,
                JsonSerializer.Serialize(settings.CustomPresets, Options));
        }
        catch { }
    }

    private static string BuildConfigPath(string fileName)
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(root, "RadioMaker", fileName);
    }
}
