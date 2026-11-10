using System.Collections.ObjectModel;
using Material.Icons;
using System.Linq;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del panel de configuracion unificado.
///
/// Layout: barra lateral con secciones a la izquierda + contenido a la
/// derecha (estilo Google Chrome). Cada seccion tiene un titulo, un icono
/// y un VM cuyo tipo determina que control se renderiza.
///
/// Secciones:
///   1. Fade
///   2. Silencios
///   3. Normalizacion
///   4. Ducking
///   5. Buses de salida     (ex ventana "Dispositivos de salida")
///   6. Ruteo por modulo    (ex pestaña "Salidas")
///   7. VU Meter            (ex ventana "Configuracion del VU meter")
///   8. Plugins de formato  (ex ventana "Plugins de formato")
///   9. Apariencia
///
/// Los cambios se aplican en vivo sobre los mismos objetos que usa el
/// MainWindowViewModel, asi que no hay boton "Aplicar". La unica excepcion
/// es la config de buses, que se persiste al cerrar la ventana.
/// </summary>
public sealed class AudioSettingsWindowViewModel : ViewModelBase
{
    // ───── Paneles individuales (referencias directas) ───────────────────
    public FadePanelViewModel FadePanel { get; }
    public SilencePanelViewModel SilencePanel { get; }
    public NormalizationPanelViewModel NormalizationPanel { get; }
    public DuckingPanelViewModel DuckingPanel { get; }
    public AudioBusesWindowViewModel BusesPanel { get; }
    public RoutingPanelViewModel RoutingPanel { get; }
    public VuMeterSettingsWindowViewModel VuMeterPanel { get; }
    public PluginsWindowViewModel PluginsPanel { get; }
    public AppearancePanelViewModel AppearancePanel { get; }
    public LocutionPanelViewModel LocutionPanel { get; }
    public BehaviorSettingsViewModel BehaviorPanel { get; }
    public EmergencyPanelViewModel EmergencyPanel { get; }

    /// <summary>Referencia al VM principal (para el ruteo).</summary>
    public MainWindowViewModel? Main { get; }

    // ───── Navegacion ────────────────────────────────────────────────────
    public ObservableCollection<SettingsSectionViewModel> Sections { get; }

    private SettingsSectionViewModel? _selectedSection;
    public SettingsSectionViewModel? SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    // ───── Iconos de la barra lateral (Material Design) ──────────────────
    private static readonly MaterialIconKind IconFade = MaterialIconKind.Waveform;
    private static readonly MaterialIconKind IconSilence = MaterialIconKind.VolumeOff;
    private static readonly MaterialIconKind IconNorm = MaterialIconKind.Tune;
    private static readonly MaterialIconKind IconDucking = MaterialIconKind.Microphone;
    private static readonly MaterialIconKind IconBuses = MaterialIconKind.SpeakerMultiple;
    private static readonly MaterialIconKind IconRouting = MaterialIconKind.CallSplit;
    private static readonly MaterialIconKind IconVuMeter = MaterialIconKind.Gauge;
    private static readonly MaterialIconKind IconPlugins = MaterialIconKind.Puzzle;
    private static readonly MaterialIconKind IconAppearance = MaterialIconKind.ThemeLightDark;
    private static readonly MaterialIconKind IconClimate = MaterialIconKind.WeatherPartlyCloudy;
    private static readonly MaterialIconKind IconBehavior = MaterialIconKind.Cog;

    // ───── Constructor ───────────────────────────────────────────────────

    public AudioSettingsWindowViewModel(
        FadePanelViewModel fadePanel,
        SilencePanelViewModel silencePanel,
        NormalizationPanelViewModel normalizationPanel,
        DuckingPanelViewModel duckingPanel,
        AudioBusesWindowViewModel busesPanel,
        VuMeterSettingsWindowViewModel vuMeterPanel,
        PluginsWindowViewModel pluginsPanel,
        LocutionPanelViewModel locutionPanel,
        BehaviorSettingsViewModel behaviorPanel,
        EmergencyPanelViewModel emergencyPanel,
        MainWindowViewModel? main = null)
    {
        FadePanel = fadePanel;
        SilencePanel = silencePanel;
        NormalizationPanel = normalizationPanel;
        DuckingPanel = duckingPanel;
        BusesPanel = busesPanel;
        VuMeterPanel = vuMeterPanel;
        PluginsPanel = pluginsPanel;
        LocutionPanel = locutionPanel;
        BehaviorPanel = behaviorPanel;
        EmergencyPanel = emergencyPanel;
        Main = main;
        RoutingPanel = new RoutingPanelViewModel(main);
        AppearancePanel = new AppearancePanelViewModel();

        Sections = new ObservableCollection<SettingsSectionViewModel>
        {
            new("fade",          "Fade",                IconFade,       fadePanel),
            new("silence",       "Silencios",           IconSilence,    silencePanel),
            new("normalization", "Normalizacion",       IconNorm,       normalizationPanel),
            new("ducking",       "Ducking",             IconDucking,    duckingPanel),
            new("behavior",      "Comportamiento",      IconBehavior,   behaviorPanel),
            new("buses",         "Buses de salida",     IconBuses,      busesPanel),
            new("routing",       "Ruteo por modulo",    IconRouting,    RoutingPanel),
            new("climate",       "Datos climaticos",    IconClimate,    locutionPanel),
            new("vumeter",       "VU Meter",            IconVuMeter,    vuMeterPanel),
            new("plugins",       "Plugins de formato",  IconPlugins,    pluginsPanel),
            new("appearance",    "Apariencia",          IconAppearance, AppearancePanel),
        };

        SelectedSection = Sections.FirstOrDefault();
    }

    /// <summary>Selecciona una seccion por su clave (util para abrir la ventana en una seccion concreta).</summary>
    public void SelectSection(string key)
    {
        var s = Sections.FirstOrDefault(x => x.Key == key);
        if (s is not null) SelectedSection = s;
    }
}
