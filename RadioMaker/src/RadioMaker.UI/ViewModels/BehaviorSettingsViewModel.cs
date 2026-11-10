using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Playback;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel de la seccion "Comportamiento" del panel de configuracion.
///
/// Contiene las opciones globales que afectan como se comporta el sistema
/// frente a interrupciones y transiciones. Es la unica fuente de verdad
/// para <see cref="PlaylistInterruptionBehavior"/>: cuando cambia, notifica
/// al host via <see cref="BehaviorChanged"/> para que aplique el cambio a
/// TODOS los players de playlist (legacy + FASE B).
/// </summary>
public sealed class BehaviorSettingsViewModel : ViewModelBase
{
    private readonly InterruptionBehaviorSettings _settings;

    /// <summary>
    /// Callback que el MainWindowViewModel setea para aplicar el cambio a
    /// todos los players de playlist. Se invoca cada vez que el usuario
    /// cambia el combo en la UI.
    /// </summary>
    public Action<PlaylistInterruptionBehavior>? BehaviorChanged { get; set; }

    /// <summary>
    /// Panel de configuracion de emergencia. Se inyecta desde
    /// MainWindowViewModel. Puede ser null en tests.
    /// </summary>
    public EmergencyPanelViewModel? EmergencyPanel { get; set; }

    public BehaviorSettingsViewModel()
    {
        _settings = InterruptionBehaviorSettingsStore.Load();
    }

    public PlaylistInterruptionBehavior InterruptionBehavior
    {
        get => _settings.Behavior;
        set
        {
            if (_settings.Behavior == value) return;
            _settings.Behavior = value;
            InterruptionBehaviorSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(InterruptionBehaviorText));

            try { BehaviorChanged?.Invoke(value); } catch { }
        }
    }

    public string InterruptionBehaviorText => InterruptionBehavior switch
    {
        PlaylistInterruptionBehavior.Continue => "Continuar donde quedo",
        PlaylistInterruptionBehavior.SkipToNext => "Saltar al siguiente item",
        _ => "?"
    };

    public PlaylistInterruptionBehavior[] AvailableInterruptionBehaviors { get; } =
        { PlaylistInterruptionBehavior.Continue, PlaylistInterruptionBehavior.SkipToNext };
}