using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using RadioMaker.Core.Logging;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.Services;

/// -------------
/// Servicio central de temas.
///
/// Aplica la preferencia elegida por el usuario (System / Light / Dark) al
/// RequestedThemeVariant de la aplicacion. Al cambiar el variant, Avalonia
/// intercambia automaticamente los valores de ThemeDictionaries en
/// ThemeResources.axaml, y todos los {DynamicResource ...} de la UI se
/// actualizan en vivo.
///
/// Cuando la preferencia es System, se suscribe a los cambios de tema del
/// sistema operativo y los aplica automaticamente.
/// -------------
public static class ThemeService
{
    private static ThemePreference _current = ThemePreference.System;
    private static bool _listeningSystem;
    private static bool _initialized;

    /// -------------Se dispara cuando cambia la preferencia (no cuando el SO cambia).</summary>
    public static event EventHandler<ThemePreference>? PreferenceChanged;

    /// -------------
    /// Se dispara cuando el ThemeVariant EFECTIVO cambia (por preferencia o
    /// por cambio de tema del SO). Los VMs suscritos deben refrescar sus
    /// propiedades de color.
    /// -------------
    public static event EventHandler? ThemeChanged;

    /// -------------Indica si el variant efectivo actual es Dark (o Light).</summary>
    public static bool IsDarkVariant => _lastAppliedVariant == ThemeVariant.Dark;

    private static ThemeVariant? _lastAppliedVariant;

    /// -------------Preferencia actual.</summary>
    public static ThemePreference Current => _current;

    /// -------------
    /// Inicializa el servicio: carga la preferencia guardada y la aplica.
    /// Llamar una sola vez al arrancar la app, antes de crear la MainWindow.
    /// -------------
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _current = ThemeSettingsStore.Load();
        ApplyVariant(_current);
        StartSystemListener();
    }

    /// -------------
    /// Cambia la preferencia. Si es System, empieza a escuchar cambios del
    /// SO; si no, se deja de escuchar.
    /// -------------
    public static void SetPreference(ThemePreference pref)
    {
        if (_current == pref) return;

        _current = pref;
        ThemeSettingsStore.Save(pref);
        ApplyVariant(pref);
        StartSystemListener();
        AppLogger.Info("SETTINGS APPEARANCE", $"Theme preference={pref}.");

        PreferenceChanged?.Invoke(null, pref);
    }

    /// -------------Fuerza la reaplicacion del variant actual (por si algo lo piso).</summary>
    public static void Refresh()
    {
        ApplyVariant(_current);
    }

    // ───── Internals ─────────────────────────────────────────────────────

    private static void ApplyVariant(ThemePreference pref)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = pref switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,   // Default = seguir al sistema
        };

        // Despues de aplicar, calcular el variant EFECTIVO.
        var effective = app.ActualThemeVariant;
        if (_lastAppliedVariant != effective)
        {
            _lastAppliedVariant = effective;
            // Notificar en el hilo de UI.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ThemeChanged?.Invoke(null, EventArgs.Empty));
        }
    }

    /// -------------
    /// Se suscribe al evento de cambio de tema del sistema operativo.
    /// Solo se activa cuando la preferencia es System.
    /// -------------
    private static void StartSystemListener()
    {
        var app = Application.Current;
        if (app is null) return;

        var settings = app.PlatformSettings;
        if (settings is null) return;

        // Solo nos interesa escuchar cuando estamos en modo System.
        if (_current != ThemePreference.System)
        {
            if (_listeningSystem)
            {
                settings.ColorValuesChanged -= OnSystemColorValuesChanged;
                _listeningSystem = false;
            }
            return;
        }

        if (_listeningSystem) return;

        settings.ColorValuesChanged += OnSystemColorValuesChanged;
        _listeningSystem = true;
    }

    private static void OnSystemColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (_current != ThemePreference.System) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Refresh();

            // Detectar si el variant efectivo cambio tras el refresh.
            var app = Application.Current;
            if (app is null) return;

            var effective = app.ActualThemeVariant;
            if (_lastAppliedVariant != effective)
            {
                _lastAppliedVariant = effective;
                ThemeChanged?.Invoke(null, EventArgs.Empty);
            }
        });
    }
}
