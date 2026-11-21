using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel "Apariencia" del panel de configuracion.
/// Permite elegir entre tres preferencias de tema:
///   - Sistema: sigue el tema del SO.
///   - Claro:   siempre claro.
///   - Oscuro:  siempre oscuro.
///
/// Los cambios se aplican en vivo a traves del ThemeService.
/// -------------
public sealed partial class AppearancePanelViewModel : ViewModelBase
{
    [ObservableProperty] private ThemePreference _preference;

    public ThemePreference[] AvailablePreferences { get; } =
    {
        ThemePreference.System,
        ThemePreference.Light,
        ThemePreference.Dark
    };

    public AppearancePanelViewModel()
    {
        _preference = ThemeService.Current;

        // Si el tema cambia desde afuera (por ejemplo desde el boton rapido
        // de la toolbar), reflejarlo en el selector.
        ThemeService.PreferenceChanged += (_, pref) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Preference != pref)
                {
                    // Evitar loop de feedback.
                    _suppressApply = true;
                    try { Preference = pref; }
                    finally { _suppressApply = false; }
                }
            });
        };
    }

    private bool _suppressApply;

    partial void OnPreferenceChanged(ThemePreference value)
    {
        if (_suppressApply) return;
        ThemeService.SetPreference(value);
    }
}