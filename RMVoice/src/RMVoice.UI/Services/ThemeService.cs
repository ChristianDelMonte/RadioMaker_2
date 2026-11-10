using Avalonia;
using Avalonia.Styling;
using RMVoice.UI.Configuration;

namespace RMVoice.UI.Services;

/// <summary>
/// Servicio central de temas de la UI de RMVoice.
///
/// Aplica la preferencia (System / Light / Dark) al RequestedThemeVariant
/// de la Application. Al cambiar el variant, Avalonia intercambia los
/// ThemeDictionaries definidos en App.axaml, y todos los {DynamicResource}
/// de la UI se actualizan en vivo.
///
/// Se usa principalmente el toggle rapido (Light ↔ Dark), pero el modelo
/// soporta las 3 opciones por si en el futuro se agrega un selector.
/// </summary>
public static class ThemeService
{
    private static ThemePreference _current = ThemePreference.Light;
    private static bool _initialized;

    public static ThemePreference Current => _current;

    public static bool IsDarkVariant => _current == ThemePreference.Dark;

    /// <summary>Se dispara cuando cambia la preferencia de tema.</summary>
    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _current = ThemeSettingsStore.Load();
        ApplyVariant(_current);
    }

    /// <summary>
    /// Alterna entre Light y Dark (el boton rapido del header).
    /// No usa System: el usuario decide explicitamente.
    /// </summary>
    public static void Toggle()
    {
        var next = _current == ThemePreference.Dark
            ? ThemePreference.Light
            : ThemePreference.Dark;

        SetPreference(next);
    }

    public static void SetPreference(ThemePreference pref)
    {
        if (_current == pref) return;

        _current = pref;
        ThemeSettingsStore.Save(pref);
        ApplyVariant(pref);

        try { ThemeChanged?.Invoke(null, EventArgs.Empty); } catch { }
    }

    private static void ApplyVariant(ThemePreference pref)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = pref switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}