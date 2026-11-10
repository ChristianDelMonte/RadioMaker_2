using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.Configuration;

/// <summary>
/// Configuracion persistente del fade automatico (para audio general) y
/// del crossfade entre carts (para la botonera).
/// </summary>
public sealed partial class FadeSettings : ObservableObject
{
    // ───── Auto-fade general ─────────────────────────────────────────────

    /// <summary>Si true, todo audio aplica fade-in y fade-out automatico.</summary>
    [ObservableProperty] private bool _isEnabled = false;

    /// <summary>Duracion del fade en segundos (aplica al inicio y al final).</summary>
    [ObservableProperty] private double _seconds = 2.0;

    // ───── Crossfade entre carts (botonera) ─────────────────────────────

    /// <summary>
    /// Si true, al disparar un cart mientras suena otro se hace fade-out
    /// del que suena y fade-in del nuevo (crossfade).
    /// Si false, los carts se superponen (comportamiento clasico).
    /// </summary>
    [ObservableProperty] private bool _cartCrossfadeEnabled = false;

    /// <summary>
    /// Duracion del crossfade entre carts, en segundos. Default 0.5 s.
    /// Valores muy chicos (menos de 0.1 s) pueden sonar bruscos.
    /// </summary>
    [ObservableProperty] private double _cartCrossfadeSeconds = 0.5;

    // ───── Helpers ───────────────────────────────────────────────────────

    /// <summary>Crossfade en milisegundos (conversion practica).</summary>
    public int CartCrossfadeMs => (int)Math.Round(CartCrossfadeSeconds * 1000.0);

    /// <summary>Texto con formato "X.X s".</summary>
    public string CartCrossfadeSecondsText => $"{CartCrossfadeSeconds:F1} s";
}

public static class FadeSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string ConfigPath { get; } = BuildPath();

    public static FadeSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<FadeSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch { }
        return new FadeSettings();
    }

    public static void Save(FadeSettings settings)
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
        return Path.Combine(root, "RadioMaker", "fade.json");
    }
}