using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Localization;
using RadioMaker.UI.Services;
using RMVoice.Core.Services;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _topPanelTimer;
    private readonly DispatcherTimer _tipTimer;
    private readonly DispatcherTimer _autoDjTimer;

    private IAudioChannel? _currentChannel;

    // ───── Log ───────────────────────────────────────────────────────────
    private readonly LogService _logService;

    // ───── Scheduler + PlaybackEngine + Configurador ─────────────────────
    private readonly SchedulerService _scheduler;
    private readonly PlaybackEngine _playback;
    private readonly ChannelConfigurator _configurator;
    private readonly PlaylistPlayer _playlistPlayer;
    private readonly PlaylistPlayer _autoDjPlayer;
    private readonly CartBoardPlayer _cartPlayer;
    private readonly BusExclusivityCoordinator _coordinator;
    private readonly EncadenadoresRegistry _encadenadoresRegistry = new();
    private readonly EmergencyWatchdogService _emergency;
    private readonly EmergencySettings _emergencySettings;

    // Presentación inicial (onboarding). Null si ya se completó o no aplica.
    private RadioMaker.UI.Demo.DemoService? _demoService;

    public BusExclusivityCoordinator Coordinator => _coordinator;
    public EncadenadoresRegistry Encadenadores => _encadenadoresRegistry;

    /// -------------
    /// Motor de audio compartido. Expuesto publicamente para evitar
    /// reflection en la UI (por ejemplo en MainWindow.axaml.cs).
    /// -------------
    public IAudioEngine AudioEngine => _engine;

    // ───── Locución (RMVoice engine) ─────────────────────────────────────
    /// -------------
    /// Servicio de locución de RMVoice. Se inicializa despues del motor
    /// de audio. Si RMVoice no esta disponible, queda en modo "no op" y
    /// la app sigue funcionando normalmente.
    /// -------------
    private readonly LocutionService _locution;

    /// -------------Configuracion persistente del sistema de locución.</summary>
    private readonly LocutionSettings _locutionSettings;

    // ───── Carpetas favoritas (panel derecho) ────────────────────────────
    /// -------------
    /// Registry central de carpetas favoritas. Se usa desde el panel
    /// derecho (menu "+") y desde el context menu del arbol del explorador.
    /// -------------
    public FavoriteFolderRegistry FavoriteFolders { get; }

    // ───── Routing de buses por modulo ───────────────────────────────────
    private readonly ModuleRoutingService _routing;

    // ===== INTEGRACIÓN EXCLUSIVIDAD =====
    /// -------------
    /// Acceso público al servicio de ruteo (para que la UI de configuración
    /// pueda modificar el AirBusId).
    /// -------------
    public ModuleRoutingService Routing => _routing;

    public ModuleBusSelectorViewModel BusSelectorCart { get; }
    public ModuleBusSelectorViewModel BusSelectorPlaylist { get; }
    public ModuleBusSelectorViewModel BusSelectorScheduler { get; }
    public ModuleBusSelectorViewModel BusSelectorFileManager { get; }

    /// -------------
    /// Toggle del modo "un solo bus para todo". Bindea al panel de
    /// configuracion de audio y a la toolbar.
    /// -------------
    public bool UseGlobalBus
    {
        get => _routing?.UseGlobalBus ?? true;
        set
        {
            if (_routing is null) return;
            if (_routing.UseGlobalBus == value) return;
            _routing.SetUseGlobalBus(value);
            OnPropertyChanged();
        }
    }

    // ───── Buses ─────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<IAudioBus> _buses = new();
    [ObservableProperty] private IAudioBus? _selectedBus;
    public string SelectedBusDisplayText => string.Format(
        LocalizationService.GetString("Loc.Main.SelectedBus"), SelectedBus?.DisplayName ?? string.Empty);

    // ───── Archivo / reproduccion ────────────────────────────────────────
    [ObservableProperty] private string _currentFilePath = string.Empty;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.Main.Status.Ready");
    private string? _statusMessageResourceKey = "Loc.Main.Status.Ready";
    private object?[] _statusMessageResourceArgs = Array.Empty<object?>();
    private bool _settingLocalizedStatus;
    [ObservableProperty] private bool _isAutoDjEnabled;
    [ObservableProperty] private string _autoDjStatusText = LocalizationService.GetString("Loc.Main.AutoDj.Status.Disabled");
    private string? _autoDjStatusResourceKey;
    private object?[] _autoDjStatusResourceArgs = Array.Empty<object?>();
    private bool _settingLocalizedAutoDjStatus;
    /// -------------Tip de ayuda actual mostrado en la barra de estado inferior.</summary>
    [ObservableProperty] private AppTip _currentTip = AppTips.GetRandom();

    /// -------------Índice del tip actual (para avanzar secuencialmente).</summary>
    private int _tipIndex;
    [ObservableProperty] private string _lastError = string.Empty;
    [ObservableProperty] private PlaybackState _playbackState = PlaybackState.Stopped;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private double _volume = 1.0;

    // ───── Info de la barra de titulo ────────────────────────────────────

    /// -------------Nombre de la aplicacion que se muestra en la barra de titulo.</summary>
    public string AppName => "RadioMaker";

    /// -------------Version de la aplicacion (auto-generada en cada build).</summary>
    public string AppVersionText => Services.AppVersionInfo.VersionText;

    /// -------------Fecha de compilacion (auto-generada en cada build).</summary>
    public string AppBuildText => Services.AppVersionInfo.BuildText;

    /// -------------Eslogan de la aplicacion.</summary>
    public string AppSlogan => LocalizationService.GetString("Loc.App.Slogan");

    // ───── Indicador "AL AIRE" ───────────────────────────────────────────

    /// -------------
    /// True si hay algo reproduciendo en el bus del Scheduler.
    /// -------------
    public bool IsAlAire => _isAlAire;

    /// -------------Fondo del panel "AL AIRE" (rojo translucido o transparente).</summary>
    public IBrush AlAireBackground => _isAlAire
        ? new SolidColorBrush(Color.Parse("#33EF4444"))   // rojo translucido
        : new SolidColorBrush(Colors.Transparent);

    /// -------------Borde del panel "AL AIRE".</summary>
    public IBrush AlAireBorderBrush => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))
        : (ResolveThemeBrush("BorderPrimary") ?? new SolidColorBrush(Color.Parse("#2A2A35")));

    /// -------------Color del LED indicador.</summary>
    public IBrush AlAireLedColor => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))     // rojo brillante
        : new SolidColorBrush(Color.Parse("#6B7280"));    // gris apagado

    /// -------------Color del texto del indicador.</summary>
    public IBrush AlAireTextColor => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))
        : (ResolveThemeBrush("FgMuted") ?? new SolidColorBrush(Color.Parse("#6B7280")));

    /// -------------Texto del indicador.</summary>
    public string AlAireText => LocalizationService.GetString(_isAlAire
        ? "Loc.Main.OnAir.Live"
        : "Loc.Main.OnAir.OffAir");


    // ───── Panel superior informativo ────────────────────────────────────

    /// -------------Titulo del audio en reproduccion (scheduler o playlist).</summary>
    [ObservableProperty] private string _nowPlayingTitle = string.Empty;

    /// -------------Artista u origen del audio en reproduccion.</summary>
    [ObservableProperty] private string _nowPlayingArtist = string.Empty;

    /// -------------
    /// Canal activo del panel "En reproduccion" (scheduler > playlist > cart).
    /// Es el canal del que se obtiene el FFT para el mini visualizador.
    /// Se actualiza desde RefreshNowPlayingPanel.
    /// -------------
    [ObservableProperty] private IAudioChannel? _nowPlayingChannel;

    /// -------------Espectro mostrado en el mini visualizador, incluso para locuciones RMVoice.</summary>
    public IAudioSpectrum? NowPlayingSpectrum =>
        (_locution.IsPlaying ? _locution.CurrentSpectrum : null) ?? NowPlayingChannel?.Spectrum;

    /// -------------
    /// True si el panel "En reproduccion" tiene contenido (hay algo sonando
    /// en scheduler o playlist). Controla el color cyan activo del panel.
    /// -------------
    [ObservableProperty] private bool _isNowPlayingActive;

    /// -------------
    /// True si el panel "Siguiente" tiene contenido real (no es "(sin proximo)").
    /// Controla el color cyan activo del panel.
    /// -------------
    [ObservableProperty] private bool _isNextPlayingActive;

    /// -------------Posicion dentro de la lista (ej: "Pista 3 de 8") o vacio.</summary>
    [ObservableProperty] private string _nowPlayingPosition = string.Empty;

    /// -------------Titulo del proximo item a reproducir, o "(sin proximo)".</summary>
    [ObservableProperty] private string _nextTitle = LocalizationService.GetString("Loc.Main.Next.None");

    /// -------------Artista u origen del proximo item.</summary>
    [ObservableProperty] private string _nextArtist = string.Empty;

    /// -------------Posicion del proximo item.</summary>
    [ObservableProperty] private string _nextPosition = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNextCountdown))]
    private string _nextCountdownText = string.Empty;

    public bool ShowNextCountdown => NextCountdownText.Length > 0;

    // ───── Brushes de los paneles "En reproduccion" / "Siguiente" ────────
    //
    // Se resuelven en el VM (no en el XAML) porque los converters
    // BoolToThemeBrushConverter solo se ejecutan cuando el booleano cambia,
    // no cuando cambia el tema. Notificamos estas propiedades desde el
    // handler de ThemeService.ThemeChanged para que se actualicen en vivo.

    /// -------------Fondo del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelBackground =>
        ResolveThemeBrush(IsNowPlayingActive ? "PanelLedActiveBg" : "PanelLedInactiveBg");

    /// -------------Borde del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelBorderBrush =>
        ResolveThemeBrush(IsNowPlayingActive ? "AccentSoft" : "BorderPrimary");

    /// -------------Color de texto del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelForeground =>
        ResolveThemeBrush(IsNowPlayingActive ? "LedActive" : "LedInactive");

    /// -------------Fondo del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelBackground =>
        ResolveThemeBrush(IsNextPlayingActive ? "PanelLedActiveBg" : "PanelLedInactiveBg");

    /// -------------Borde del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelBorderBrush =>
        ResolveThemeBrush(IsNextPlayingActive ? "AccentSoft" : "BorderPrimary");

    /// -------------Color de texto del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelForeground =>
        ResolveThemeBrush(IsNextPlayingActive ? "LedActive" : "LedInactive");

    /// -------------
    /// Fondo del panel que contiene el VU meter. Theme-aware: casi negro
    /// en modo oscuro, cyan claro en modo claro. Se usa como Background
    /// del Border que envuelve el VuMeter en TopInfoPanel.
    /// -------------
    public IBrush VuPanelBackground => new SolidColorBrush(
        ThemeService.IsDarkVariant
            ? Color.Parse("#0A0A0C")
            : Color.Parse("#D0EEF5"));

    /// -------------
    /// True si hay algun audio reproduciendose ahora mismo en cualquier
    /// modulo (scheduler, playlist, cart o canal manual). Controla el
    /// encendido/apagado del LED de tiempo.
    /// -------------
    [ObservableProperty] private bool _isAnyAudioPlaying;

    /// -------------Tiempo transcurrido del audio actual (formato HH:MM:SS).</summary>
    [ObservableProperty] private string _elapsedText = "00:00:00";

    /// -------------Tiempo restante del audio actual (formato -HH:MM:SS).</summary>
    [ObservableProperty] private string _remainingText = "-00:00:00";

    /// -------------Fecha actual (formato DD-MM-YY, compatible con el LED).</summary>
    [ObservableProperty] private string _dateText = "00-00-00";

    /// -------------Hora actual (formato HH:MM:SS).</summary>
    [ObservableProperty] private string _timeText = "00:00:00";

    /// -------------Localidad o provincia para el panel de clima.</summary>
    [ObservableProperty] private string _weatherLocation = "—";
    /// -------------Info del clima (temperatura / humedad).</summary>
    [ObservableProperty] private string _weatherInfo = "—";

    // ───── Niveles VU ────────────────────────────────────────────────────
    [ObservableProperty] private double _vuLeftLevel;
    [ObservableProperty] private double _vuRightLevel;
    [ObservableProperty] private double _vuLeftPeak;
    [ObservableProperty] private double _vuRightPeak;
    [ObservableProperty] private double _vuLeftRms;
    [ObservableProperty] private double _vuRightRms;
    [ObservableProperty] private bool _vuClip;

    // ───── Locución (RMVoice) ────────────────────────────────────────────

    /// -------------
    /// Servicio de locución de RMVoice. La UI lo usa para:
    ///   - Consultar si esta disponible (habilitar/deshabilitar opciones).
    ///   - Disparar locuciones a demanda (botones rapidos).
    ///   - Refrescar el clima cada X minutos.
    ///   - Leer el ultimo snapshot de clima para mostrarlo en el panel.
    /// -------------
    public LocutionService Locution => _locution;

    /// -------------
    /// True si RMVoice esta cargado e inicializado. La UI usa esto para
    /// habilitar/deshabilitar las opciones de locución.
    /// -------------
    public bool IsLocutionAvailable => _locution.IsAvailable;

    // ───── Paneles hijos ─────────────────────────────────────────────────
    public VuMeterSettings VuSettings { get; }
    public EqualizerSettings EqSettings { get; }
    public SilencePanelSettings SilenceSettings { get; }
    public NormalizationSettings NormSettings { get; }
    /// -------------
    /// Panel del clima + botones de locución. Se muestra en el TopInfoPanel.
    /// -------------
    public LocutionPanelViewModel LocutionPanel { get; }

    public EqualizerPanelViewModel Equalizer { get; }
    public SilencePanelViewModel Silence { get; }
    public NormalizationPanelViewModel Normalization { get; }
    public FadePanelViewModel Fade { get; }
    public BehaviorSettingsViewModel Behavior { get; }
    public EmergencyPanelViewModel Emergency { get; }
    public EmergencyWatchdogService EmergencyService => _emergency;
    // Presentación inicial. Null si no aplica (DemoCompleted o manifest faltante).
    public RadioMaker.UI.Demo.DemoViewModel? Demo { get; private set; }
    public bool IsEmergencyActive => _emergency.IsActive;
    public string EmergencyStatusText => _emergency.IsActive
        ? $"{_emergency.CurrentTrackTitle} — pista {_emergency.CurrentTrackIndex} de {_emergency.CurrentTrackTotal} en {_emergency.CurrentBusDisplayName}"
        : "";
    public DuckingPanelViewModel Ducking { get; }
    public SchedulerPanelViewModel Scheduler { get; }
    public PlaybackLogViewModel PlaybackLog { get; }
    public SpectrumPanelViewModel Spectrum { get; }

    public TabHostViewModel TabHost { get; }
    public TabHostViewModel TabHostCenter { get; }
    public TabHostViewModel TabHostRight { get; }
    public CartBoardViewModel CartBoard { get; }
    public FileSystemBrowserViewModel FileBrowser { get; }

    /// -------------Lista de players de carts extras, para poder disponerlos al cerrar.</summary>
    private readonly List<CartBoardPlayer> _extraCartPlayers = new();

    /// -------------
    /// Lista de players de playlist activos (uno por pestaña). Se usan
    /// para poder aplicar cambios de config en vivo y liberar al cerrar
    /// la pestaña. (FASE B)
    /// -------------
    private readonly List<PlaylistPlayer> _playlistPlayers = new();
    private Playlist? _autoDjPlaylist;
    private Playlist? _autoDjPreparedPlaylist;
    private PlaylistEditorViewModel? _autoDjEditorVm;
    private static string AutoDjTemporaryPlaylistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RadioMaker", "AutoDJ", "AutoDJ-temporal.rmf");
    private DateTime _autoDjNextStartAttemptUtc;

    // ───── Propiedades derivadas ─────────────────────────────────────────
    public string PositionText => $"{Position:mm\\:ss\\.f} / {Duration:mm\\:ss\\.f}";
    public string VolumeText => Volume.ToString("P0");
    public IReadOnlyList<string> SupportedExtensions => _engine.SupportedExtensions;

    // ───── Pestaña activa y qué se puede guardar ────────────────────────

    /// -------------
    /// Pestaña activa del ultimo TabHost que recibio foco. Puede ser null
    /// si no hay ninguna pestaña seleccionada.
    /// -------------
    public TabItemViewModel? ActiveTab => _lastActiveTabHost?.SelectedTab;

    public bool CanSaveTanda => ActiveTab?.Content is TandaEditorViewModel;
    public bool CanSaveScheduledList => ActiveTab?.Content is ScheduledListViewModel;
    public bool CanSavePlaylist => ActiveTab?.Content is PlaylistEditorViewModel;
    public bool CanSaveCart => ActiveTab?.Content is CartBoardViewModel;
    public bool CanSaveNotes => ActiveTab?.Content is NotesEditorViewModel;

    /// -------------
    /// Notifica todas las propiedades relacionadas con la pestaña activa.
    /// Se llama cuando cambia la seleccion en cualquier TabHost.
    /// -------------
    private void NotifyActiveTabChanged()
    {
        OnPropertyChanged(nameof(ActiveTab));

        // Guardar
        OnPropertyChanged(nameof(CanSaveTanda));
        OnPropertyChanged(nameof(CanSaveScheduledList));
        OnPropertyChanged(nameof(CanSavePlaylist));
        OnPropertyChanged(nameof(CanSaveCart));
        OnPropertyChanged(nameof(CanSaveNotes));

        // Editar
        OnPropertyChanged(nameof(CanEditBlock));
        OnPropertyChanged(nameof(CanEditTandaItem));
        OnPropertyChanged(nameof(CanEditPlaylistItem));
        OnPropertyChanged(nameof(CanEditAudioTags));
    }

    /// -------------
    /// Cambia la suscripcion al VM de contenido del tab activo. Se llama
    /// cada vez que cambia la pestaña seleccionada en cualquier TabHost.
    /// -------------
    private void SwitchActiveContent(TabItemViewModel? tab)
    {
        if (_activeContentVm is not null)
        {
            _activeContentVm.PropertyChanged -= OnActiveContentPropertyChanged;
            _activeContentVm = null;
        }

        if (tab?.Content is System.ComponentModel.INotifyPropertyChanged vm)
        {
            _activeContentVm = vm;
            vm.PropertyChanged += OnActiveContentPropertyChanged;
        }

        NotifyActiveTabChanged();
    }

    private void OnActiveContentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Cuando cambia la seleccion dentro del contenido activo, refrescar
        // las propiedades de los menus.
        if (e.PropertyName == nameof(ScheduledListViewModel.SelectedItem)
            || e.PropertyName == nameof(PlaylistEditorViewModel.SelectedTrack)
            || e.PropertyName == nameof(CartBoardViewModel.SelectedButton))
        {
            NotifyActiveTabChanged();
        }
    }

    // ───── Propiedades de "Editar" ───────────────────────────────────────

    /// -------------
    /// True si la pestaña activa es un Programado con un item de tipo Bloque
    /// seleccionado. Habilita "Editar > Bloque...".
    /// -------------
    public bool CanEditBlock =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Block;

    /// -------------
    /// True si la pestaña activa es un Programado con un item de tipo Tanda
    /// seleccionado. Habilita "Editar > Tanda...".
    /// -------------
    public bool CanEditTandaItem =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Tanda;

    /// -------------
    /// True si la pestaña activa es un Programado con un item de tipo Playlist
    /// seleccionado. Habilita "Editar > Playlist...".
    /// -------------
    public bool CanEditPlaylistItem =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Playlist;

    /// -------------
    /// True si hay un archivo de audio seleccionado en la pestaña activa
    /// Habilita "Editar tags...".
    /// -------------
    public bool CanEditAudioTags
    {
        get
        {
            var path = GetSelectedAudioFilePath();
            if (string.IsNullOrWhiteSpace(path)) return false;

            // ===== INTEGRACIÓN RMWave =====
            // Un .rmw es un proyecto, no un audio con ID3/FLAC/MP4 tags.
            // Se edita desde RMWave, no desde el editor de tags.
            if (path.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }

    /// -------------
    /// Devuelve la ruta del archivo de audio seleccionado en la pestaña
    /// activa, o null si no hay ninguno. Excluye el File Manager.
    /// -------------
    public string? GetSelectedAudioFilePath()
    {
        // Programados: item SingleFile seleccionado.
        if (ActiveTab?.Content is ScheduledListViewModel sched
            && sched.SelectedItem is { Kind: ScheduledItemKind.SingleFile } item
            && !string.IsNullOrWhiteSpace(item.FilePath))
        {
            return item.FilePath;
        }

        // Playlist: pista seleccionada.
        if (ActiveTab?.Content is PlaylistEditorViewModel playlist
            && playlist.SelectedTrack is { } pt
            && !string.IsNullOrWhiteSpace(pt.FilePath))
        {
            return pt.FilePath;
        }

        // Cart: boton seleccionado.
        if (ActiveTab?.Content is CartBoardViewModel cart
            && cart.SelectedButton is { } cb
            && !string.IsNullOrWhiteSpace(cb.FilePath))
        {
            return cb.FilePath;
        }

        return null;
    }

    public string PlaybackStateText => PlaybackState switch
    {
        PlaybackState.Playing => LocalizationService.GetString("Loc.Main.Playback.Playing"),
        PlaybackState.Paused  => LocalizationService.GetString("Loc.Main.Playback.Paused"),
        PlaybackState.Stopped => LocalizationService.GetString("Loc.Main.Playback.Stopped"),
        PlaybackState.Ended   => LocalizationService.GetString("Loc.Main.Playback.Ended"),
        PlaybackState.Stalled => LocalizationService.GetString("Loc.Main.Playback.Stalled"),
        PlaybackState.Error   => LocalizationService.GetString("Loc.Main.Playback.Error"),
        _ => PlaybackState.ToString()
    };

    /// -------------
    /// Indica si el tema efectivo actual es oscuro. Se usa para elegir
    /// el icono del boton rapido de tema (sol cuando esta oscuro, luna
    /// cuando esta claro).
    /// -------------
    public bool IsDarkTheme => ThemeService.IsDarkVariant;
    public bool IsLightTheme => !ThemeService.IsDarkVariant;
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();

    /// -------------
    /// Indica si la ventana principal esta maximizada. Se usa para elegir
    /// el icono del boton maximizar/restaurar en la barra de titulo.
    /// -------------
    [ObservableProperty] private bool _isMaximized;

    public bool HasError => !string.IsNullOrEmpty(LastError);
    public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath);
    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public bool CanPlay => HasFile && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanStop => HasFile && PlaybackState != PlaybackState.Stopped;
    public bool HasBuses => Buses.Count > 0;

    /// -------------
    /// Ultimo TabHost que recibio foco (cambio su SelectedTab). Se usa
    /// para saber cual es la "pestaña activa" cuando el menu Guardar
    /// tiene que decidir que opcion habilitar.
    /// -------------
    private TabHostViewModel? _lastActiveTabHost;

    /// -------------
    /// VM de contenido del tab activo. Nos suscribimos a su PropertyChanged
    /// para detectar cambios de seleccion internos (SelectedItem, SelectedTrack,
    /// SelectedButton) y refrescar las propiedades de los menus Editar/Guardar.
    /// -------------
    private System.ComponentModel.INotifyPropertyChanged? _activeContentVm;

    private bool _suppressBusSelectionHook;

    private IAudioChannel? ActiveChannel
    {
        get
        {
            if (_currentChannel is not null) return _currentChannel;
            if (_playback.CurrentChannel is not null) return _playback.CurrentChannel;

            // ── FASE B: playlists (una por pestaña)
            var pl = GetAnyPlaylistChannel();
            if (pl is not null) return pl;

            if (_autoDjPlayer.CurrentChannel is { } autoDjChannel) return autoDjChannel;

            // Cart principal + carts extras.
            var cart = GetAnyActiveCartChannel();
            if (cart is not null) return cart;

            // ── Presentación inicial (onboarding): si no hay nada más
            // sonando y la demo está activa, alimentar el VU meter con su
            // canal. Prioridad más baja que el resto para no tapar audio
            // real si por casualidad suenan en paralelo.
            var demoCh = Demo?.GetCurrentChannel();
            if (demoCh is not null) return demoCh;

            return null;
        }
    }

    /// -------------
    /// Devuelve el primer canal activo entre el cart principal y todos
    /// los carts extras (botoneras adicionales). Devuelve null si
    /// ninguno esta reproduciendo.
    /// -------------
    private IAudioChannel? GetAnyActiveCartChannel()
    {
        try
        {
            var ch = _cartPlayer.CurrentChannel;
            if (ch is not null && ch.State == PlaybackState.Playing)
                return ch;
        }
        catch { }

        foreach (var p in _extraCartPlayers)
        {
            try
            {
                var ch = p.CurrentChannel;
                if (ch is not null && ch.State == PlaybackState.Playing)
                    return ch;
            }
            catch { }
        }

        return null;
    }

    /// -------------
    /// True si el cart principal o algun cart extra esta reproduciendo.
    /// -------------
    private bool AnyCartPlaying()
    {
        try { if (_cartPlayer.HasAnyPlaying) return true; } catch { }

        foreach (var p in _extraCartPlayers)
        {
            try { if (p.HasAnyPlaying) return true; } catch { }
        }

        return false;
    }

    // =====================================================================
    // ───── CONSTRUCTOR ───────────────────────────────────────────────────
    // =====================================================================
    public MainWindowViewModel(IAudioEngine engine)
    {
        _engine = engine;

        // 0) Log service (primero de todo, para poder loguear el resto).
        // Usamos el LogService global (singleton) para que todos los
        // modulos puedan loguear sin necesidad de inyectarlo.
        _logService = AppLogger.Service;
        _logService.Info("App", "Iniciando aplicacion.");

        // 1) Cargar TODAS las settings.
        VuSettings = VuMeterSettingsStore.Load();
        EqSettings = EqualizerSettingsStore.Load();
        SilenceSettings = SilencePanelSettingsStore.Load();
        NormSettings = NormalizationSettingsStore.Load();
        Volume = VolumeSettingsStore.Load();

        // 2) Scheduler.
        var registry = new ScheduledListRegistry();
        _scheduler = new SchedulerService(registry);

        // 3) Configurador.
        _configurator = new ChannelConfigurator(EqSettings, SilenceSettings, NormSettings);

        // 3.5) BusExclusivityCoordinator (arbitra quien suena en cada bus).
        _coordinator = new BusExclusivityCoordinator(
            BusExclusivityConfigStore.Load());

        // 4) Locución (RMVoice). Se crea ANTES del PlaybackEngine para poder
        //    inyectarlo como dependencia del motor de reproducción (mix de
        //    locuciones en tandas/playlists programadas). La inicialización
        //    real (que requiere que BASS ya esté inicializado) ocurre más
        //    tarde, en InitializeEngine().
        _locution = new LocutionService();
        _locution.InterfaceLanguage = LocalizationService.CurrentLanguage;
        _locutionSettings = LocutionSettingsStore.Load();

        // 5) PlaybackEngine.
        _playback = new PlaybackEngine(
            _engine,
            _scheduler,
            action => Dispatcher.UIThread.Post(action),
            _coordinator,
            _locution,
            _encadenadoresRegistry);

        _playback.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "SCHEDULER", logDetails);

        _playback.PrewarmFile = path => _configurator.Prewarm(path);

        _configurator.AnalysisCompleted = (filePath) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // _playback, _playlistPlayer y _cartPlayer se asignan DESPUES
                // de este lambda en el constructor. En runtime ya estan
                // inicializados cuando este callback se dispara, pero el
                // compilador no puede probarlo, asi que usamos ?. por seguridad.
                try
                {
                    var schedPath = _playback?.CurrentFilePath;
                    if (!string.IsNullOrEmpty(schedPath) &&
                        string.Equals(schedPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _playback?.ReapplyCurrentChannelConfig();
                    }
                }
                catch { }

                try
                {
                    var plPath = _playlistPlayer?.CurrentFilePath;
                    if (!string.IsNullOrEmpty(plPath) &&
                        string.Equals(plPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _playlistPlayer?.ReapplyCurrentChannelConfig();
                    }
                }
                catch { }

                try { _cartPlayer?.ReapplyToMatchingChannels(filePath); } catch { }
                foreach (var cart in _extraCartPlayers)
                {
                    try { cart.ReapplyToMatchingChannels(filePath); } catch { }
                }
            });
        };

        // 5) El VM del panel de clima se crea aca, pero necesita que el engine
        // este inicializado para resolver buses. Se le pasa igual: los
        // comandos que necesitan bus verifican en runtime.
        LocutionPanel = new LocutionPanelViewModel(
            _engine,
            _locution,
            _locutionSettings,
            getActiveChannels: GetAllActiveChannels);
        LocutionPanel.PropertyChanged += OnLocutionPanelPropertyChanged;
        LocutionPanel.OpenSettingsRequested += (_, _) => OpenSettingsFromPanel();
        LocutionPanel.OpenRMVoiceRequested += (_, _) => LaunchRMVoiceUI();

        // 6) CartBoardPlayer.
        _cartPlayer = new CartBoardPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _coordinator);

        _cartPlayer.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "CART", logDetails);

        // 7) PlaylistPlayer.
        _playlistPlayer = new PlaylistPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _locution,
            _coordinator,
            _encadenadoresRegistry);

        _playlistPlayer.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "PLAYLIST", logDetails);

        _playlistPlayer.PrewarmFile = path => _configurator.Prewarm(path);

        _autoDjPlayer = new PlaylistPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _locution,
            _coordinator,
            _encadenadoresRegistry,
            ModuleRole.AutoDj);
        _autoDjPlayer.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "AUTODJ", logDetails);
        _autoDjPlayer.PrewarmFile = path => _configurator.Prewarm(path);
        _autoDjPlayer.StateChanged += OnAutoDjPlayerStateChanged;

        // 7) ViewModels de paneles.
        Equalizer = new EqualizerPanelViewModel(EqSettings);
        Silence = new SilencePanelViewModel(SilenceSettings);
        Normalization = new NormalizationPanelViewModel(NormSettings);
        Fade = new FadePanelViewModel();
        Behavior = new BehaviorSettingsViewModel();
        Behavior.BehaviorChanged = ApplyInterruptionBehaviorToAllPlaylistPlayers;

        // ===== EMERGENCIA =====
        _emergencySettings = EmergencySettingsStore.Load();

        // P14: el estado "armado" SE PERSISTE entre sesiones. Si el operador
        // dejó la emergencia activa al cerrar, al reabrir el watchdog arranca
        // monitoreando automáticamente (útil para radios 24/7 con operador de
        // guardia). El operador puede desarmar explícitamente desde la toolbar
        // o desde Configuración → Emergencia.

        _emergency = new EmergencyWatchdogService(
            _engine,
            _coordinator,
            getAirBusId: () => _routing?.AirBusId ?? string.Empty,
            dispatch: action => Dispatcher.UIThread.Post(action),
            settings: _emergencySettings);

        _emergency.ConfigureChannel = (ch, path, log) =>
            _configurator.ConfigureChannel(ch, path, "EMERGENCY", log);

        Emergency = new EmergencyPanelViewModel(_emergencySettings, _emergency);
        Emergency.SettingsChanged = s =>
        {
            _emergency.UpdateSettings(s);
            _logService.Info("SETTINGS EMERGENCY",
                $"Enabled={s.IsEnabled}; Mode={s.Mode}; Silence={s.DetectSilence} at {s.ThresholdDb:F1} dB/{s.SilenceThresholdSeconds}s; " +
                $"NoChannels={s.DetectNoChannels}; Volume={s.Volume:P0}; FadeIn={s.FadeInMs}ms; FadeOut={s.FadeOutMs}ms.");
        };

        Emergency.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EmergencyPanelViewModel.IsEnabled))
            {
                OnPropertyChanged(nameof(IsEmergencyArmed));
                OnPropertyChanged(nameof(EmergencyButtonBackground));
                OnPropertyChanged(nameof(EmergencyButtonForeground));
                OnPropertyChanged(nameof(EmergencyButtonTooltip));
            }
        };

        _emergency.EmergencyStarted += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsEmergencyActive));
            OnPropertyChanged(nameof(EmergencyStatusText));
            NotifyEmergencyButtonChanged();
        });
        _emergency.EmergencyStopped += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsEmergencyActive));
            OnPropertyChanged(nameof(EmergencyStatusText));
            NotifyEmergencyButtonChanged();
        });
        _emergency.NowPlayingChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(EmergencyStatusText));
        });


        Ducking = new DuckingPanelViewModel(DuckingSettingsStore.Load(), engine);
        Ducking.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DuckingPanelViewModel.IsEnabled))
            {
                OnPropertyChanged(nameof(IsDuckingActive));
                OnPropertyChanged(nameof(DuckingTooltip));
                OnPropertyChanged(nameof(DuckingButtonBackground));
                OnPropertyChanged(nameof(DuckingButtonForeground));
            }

            if (e.PropertyName is nameof(DuckingPanelViewModel.IsEnabled)
                or nameof(DuckingPanelViewModel.ThresholdDb)
                or nameof(DuckingPanelViewModel.ReductionDb)
                or nameof(DuckingPanelViewModel.HoldMs)
                or nameof(DuckingPanelViewModel.AttackMs)
                or nameof(DuckingPanelViewModel.ReleaseMs)
                or nameof(DuckingPanelViewModel.SelectedMic))
            {
                _logService.Info("SETTINGS DUCKING",
                    $"Enabled={Ducking.IsEnabled}; Mic={Ducking.SelectedMic?.Name ?? "(default)"}; " +
                    $"Threshold={Ducking.ThresholdDb:F1}dB; Reduction={Ducking.ReductionDb:F1}dB; " +
                    $"Hold={Ducking.HoldMs}ms; Attack={Ducking.AttackMs}ms; Release={Ducking.ReleaseMs}ms.");
            }
        };

        Scheduler = new SchedulerPanelViewModel(_scheduler, _playback, _playlistPlayer);
        PlaybackLog = new PlaybackLogViewModel(_logService);
        Spectrum = new SpectrumPanelViewModel();

        // 7.5) Registry de carpetas favoritas (carga desde disco).
        FavoriteFolders = new FavoriteFolderRegistry();

        // 8) Inicializar el engine ANTES de crear los selectores de bus,
        //    porque el ModuleRoutingService necesita que el engine ya tenga
        //    los buses cargados desde buses.json.
        InitializeEngine();

        // 9) Routing de buses por modulo.
        _routing = new ModuleRoutingService(_engine);

        BusSelectorCart = new ModuleBusSelectorViewModel(_routing, AudioModuleKind.Cart);
        BusSelectorPlaylist = new ModuleBusSelectorViewModel(_routing, AudioModuleKind.Playlist);
        BusSelectorScheduler = new ModuleBusSelectorViewModel(_routing, AudioModuleKind.Scheduler);
        BusSelectorFileManager = new ModuleBusSelectorViewModel(_routing, AudioModuleKind.FileManager);

        _routing.Changed += OnRoutingChanged;
        _routing.ModuleBusChanged += OnModuleBusChanged;

        // Sincronizar el combo global con el bus persistido en el routing.
        _suppressBusSelectionHook = true;
        try { SelectedBus = _routing.GlobalBus; }
        finally { _suppressBusSelectionHook = false; }

        // Aplicar los buses que correspondan a cada reproductor.
        ApplyRoutingToPlayers();

        // 10) Pestañas y paneles principales.
        //     ACA ya existen los BusSelectorXxx, asi que se pueden asignar.

        // Cart principal: la botonera que vive como pestaña fija del centro.
        // ── FASE C: tiene su propio SimpleBusSelector.
        CartBoard = new CartBoardViewModel(
            CartBoardStore.LoadOrCreateDefault(),
            CartBoardStore.DefaultPath,
            _cartPlayer,
            defaultBusProvider: GetCartDefaultBus);

        var primaryCartSelector = new SimpleBusSelectorViewModel(
            _engine,
            initialBus: GetCartDefaultBus(),
            onBusChanged: bus => { try { _cartPlayer.SetBus(bus); } catch { } });

        CartBoard.AttachBusSelector(primaryCartSelector);

        // Factory para carts extras: cada uno tiene su propio player.
        Func<CartBoardViewModel> cartFactory = CreateExtraCartBoard;

        // TabHost de la zona izquierda.
        TabHost = new TabHostViewModel(
            TabZone.Left,
            registry,
            _playlistPlayer,
            BusSelectorScheduler,
            BusSelectorPlaylist,
            schedulerPanel: Scheduler,
            playbackEngine: _playback,
            encadenadoresRegistry: _encadenadoresRegistry)
        {
            CartBoardFactory = cartFactory,
            CartBoardDisposer = DisposeExtraCartBoard,

            // ── FASE B: factory + disposer de playlists ────────────────
            PlaylistEditorFactory = CreatePlaylistEditorForTab,
            PlaylistEditorDisposer = DisposePlaylistEditor,
        };

        // TabHost de la zona central.
        TabHostCenter = new TabHostViewModel(
            TabZone.Center,
            registry,
            _playlistPlayer,
            BusSelectorScheduler,
            BusSelectorPlaylist,
            fixedPanel: CartBoard,
            playbackEngine: _playback,
            encadenadoresRegistry: _encadenadoresRegistry)
        {
            CartBoardFactory = cartFactory,
            CartBoardDisposer = DisposeExtraCartBoard,

            PlaylistEditorFactory = CreatePlaylistEditorForTab,
            PlaylistEditorDisposer = DisposePlaylistEditor,
        };

        // Explorador de archivos (fijo de la zona derecha).
        FileBrowser = new FileSystemBrowserViewModel(_engine, FavoriteFolders);

        FileBrowser.FileActivated += OnFileActivated;

        // TabHost de la zona derecha. La pestaña fija es el FileSystemBrowser.
        TabHostRight = new TabHostViewModel(
            TabZone.Right,
            registry,
            _playlistPlayer,
            BusSelectorScheduler,
            BusSelectorPlaylist,
            fixedPanel: FileBrowser,
            favoriteRegistry: FavoriteFolders,
            audioEngine: _engine,
            playbackEngine: _playback);

        // Registrar cual TabHost es el "activo" (ultimo que cambio de pestaña).
        // Se usa para los menus Guardar/Editar: solo se habilita la opcion
        // del tipo correspondiente al tab activo.
        _lastActiveTabHost = TabHost;

        TabHost.SelectionChanged += (_, tab) =>
        {
            _lastActiveTabHost = TabHost;
            SwitchActiveContent(tab);
        };

        TabHostCenter.SelectionChanged += (_, tab) =>
        {
            _lastActiveTabHost = TabHostCenter;
            SwitchActiveContent(tab);
        };

        TabHostRight.SelectionChanged += (_, tab) =>
        {
            _lastActiveTabHost = TabHostRight;
            SwitchActiveContent(tab);
        };

        // Inicializar el contenido activo con la pestaña inicial del host izquierdo.
        SwitchActiveContent(TabHost.SelectedTab);

        // 11) Timers.
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => RefreshPosition();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => RefreshMeter();

        _autoDjTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _autoDjTimer.Tick += (_, _) => TryStartAutoDjWhenAirIsFree();

        // Timer dedicado al panel superior (LED de tiempo + fecha/hora).
        // Corre SIEMPRE, independiente de si hay reproduccion o no, porque
        // el reloj tiene que actualizarse aunque no haya audio sonando.
        _topPanelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _topPanelTimer.Tick += (_, _) => RefreshTopPanel();
        _topPanelTimer.Start();

        // Refrescar una vez al arrancar para que el reloj no arranque en 00:00.
        RefreshTopPanel();

        // ===== TIPS ROTATIVOS =====
        // Setear el índice al tip actual (puede ser aleatorio) y avanzar
        // secuencialmente cada 90s.
        _tipIndex = Array.IndexOf(
            Enumerable.Range(0, AppTips.Count)
                .Select(AppTips.GetByIndex)
                .ToArray(),
            CurrentTip);

        if (_tipIndex < 0) _tipIndex = 0;

        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _tipTimer.Tick += (_, _) => AdvanceTip();
        _tipTimer.Start();
        LocalizationService.LanguageChanged += OnLanguageChanged;

        // Suscripcion al cambio de tema: refrescar IsDarkTheme y los brushes
        // de los paneles "En reproduccion" / "Siguiente".
        ThemeService.ThemeChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(IsLightTheme));

                OnPropertyChanged(nameof(NowPlayingPanelBackground));
                OnPropertyChanged(nameof(NowPlayingPanelBorderBrush));
                OnPropertyChanged(nameof(NowPlayingPanelForeground));

                OnPropertyChanged(nameof(NextPlayingPanelBackground));
                OnPropertyChanged(nameof(NextPlayingPanelBorderBrush));
                OnPropertyChanged(nameof(NextPlayingPanelForeground));

                OnPropertyChanged(nameof(VuPanelBackground));
            });
        };

        // 12) Suscripciones.
        _scheduler.ItemTriggered += OnSchedulerItemTriggered;
        _playback.StateChanged += OnPlaybackStateChanged;
        _playlistPlayer.StateChanged += OnPlaylistPlayerStateChanged;
        _cartPlayer.ButtonStateChanged += OnCartButtonStateChanged;
        _scheduler.StateReset += OnSchedulerStateReset;

        // Propagar cambios de EQ, Trim y Normalizacion a todos los canales
        // activos de TODOS los reproductores en tiempo real.
        Equalizer.SettingsChanged += OnProcessingSettingsChanged;
        Silence.SettingsChanged += OnProcessingSettingsChanged;
        Normalization.SettingsChanged += OnProcessingSettingsChanged;

        // Sincronizar el auto-fade del PlaybackEngine con el panel de Fade.
        Fade.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FadePanelViewModel.IsEnabled) ||
                e.PropertyName == nameof(FadePanelViewModel.Seconds))
            {
                _playback.SetAutoFade(Fade.IsEnabled, Fade.Seconds);
                _logService.Info("SETTINGS FADE", $"Auto-fade: {Fade.IsEnabled} @ {Fade.Seconds}s.");
            }
            else if (e.PropertyName == nameof(FadePanelViewModel.CartCrossfadeEnabled) ||
                     e.PropertyName == nameof(FadePanelViewModel.CartCrossfadeSeconds))
            {
                ApplyCrossfadeToCartPlayers();
                ApplyCrossfadeToPlaylistPlayers();

                // Sincronizar la duracion del crossfade del coordinator.
                _coordinator.CrossfadeDurationMs =
                    (int)Math.Round(Fade.CartCrossfadeSeconds * 1000.0);

                _logService.Info("SETTINGS CROSSFADE",
                    $"Cart/Playlist crossfade: {Fade.CartCrossfadeEnabled} @ {Fade.CartCrossfadeSeconds:F1}s.");
            }
        };

        // Aplicar valores iniciales.
        _playback.SetAutoFade(Fade.IsEnabled, Fade.Seconds);

        ApplyCrossfadeToCartPlayers();
        ApplyCrossfadeToPlaylistPlayers();

        _coordinator.CrossfadeDurationMs =
            (int)Math.Round(Fade.CartCrossfadeSeconds * 1000.0);

        // Aplicar el InterruptionBehavior persistido a TODOS los players
        // (legacy + FASE B). Antes solo se aplicaba al legacy, y los players
        // por pestaña seguian con el default hasta el primer cambio manual.
        try
        {
            var savedBehavior = InterruptionBehaviorSettingsStore.Load().Behavior;
            ApplyInterruptionBehaviorToAllPlaylistPlayers(savedBehavior);
        }
        catch (Exception ex)
        {
            _logService.Error("SETTINGS PLAYLIST", "No se pudo cargar el comportamiento de interrupción guardado", ex);
        }

        // Arrancar el watchdog de emergencia (si esta habilitado en settings,
        // el watchdog comienza a monitorear; sino, el tick es no-op).
        try
        {
            _emergency.Start();
            _logService.Info("Emergency",
                _emergencySettings.IsEnabled
                    ? "Watchdog de emergencia iniciado (habilitado)."
                    : "Watchdog de emergencia iniciado (deshabilitado — no monitorea).");
        }
        catch (Exception ex)
        {
            _logService.Error("Emergency", "No se pudo iniciar watchdog", ex);
        }

        try
        {
            _scheduler.Start();
            _logService.Info("Scheduler", "Scheduler iniciado.");
        }
        catch (Exception ex)
        {
            LastError = string.Format(LocalizationService.GetString("Loc.Main.Error.SchedulerStart"), ex.Message);
            _logService.Error("Scheduler", "No se pudo iniciar", ex);
        }

        // Presentación inicial (solo en el primer arranque).
        TryStartDemo();
    }

    /// -------------
    /// Arranca la presentación inicial si es el primer arranque del usuario.
    /// Marca el estado como completado INMEDIATAMENTE para garantizar que se
    /// vea exactamente una vez, incluso si el usuario cierra la app o crashea
    /// durante la reproducción.
    /// -------------
    private void TryStartDemo()
    {
        try
        {
            if (RadioMaker.UI.Demo.DemoStateStore.IsCompleted())
            {
                _logService.Debug("DEMO", "Presentación ya completada, se saltea.");
                return;
            }

            // Marcar como completada INMEDIATAMENTE.
            var marked = RadioMaker.UI.Demo.DemoStateStore.MarkCompleted();
            if (!marked)
            {
                _logService.Warning("DEMO",
                    "No se pudo guardar el estado de la demo. " +
                    "Es posible que vuelva a aparecer la próxima vez.");
            }

            var demoService = new RadioMaker.UI.Demo.DemoService(_engine);
            if (!demoService.TryLoad())
            {
                demoService.Dispose();
                return;
            }

            _demoService = demoService;
            Demo = new RadioMaker.UI.Demo.DemoViewModel(demoService);
            Demo.PropertyChanged += OnDemoPropertyChanged;

            if (Demo.IsActive)
            {
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
                try { UpdateSpectrumChannel(); } catch { }
            }

            OnPropertyChanged(nameof(Demo));
        }
        catch (Exception ex)
        {
            _logService.Warning("DEMO", $"No se pudo iniciar la presentación: {ex.Message}");
        }
    }

    private void OnDemoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RadioMaker.UI.Demo.DemoViewModel.IsActive))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (Demo?.IsActive == true)
            {
                // Demo arrancó: asegurar timers + spectrum apuntando a la demo.
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
                try { UpdateSpectrumChannel(); } catch { }
            }
            else
            {
                // Demo terminó: si no hay otra reproducción, parar timers
                // y resetear el VU/spectrum.
                if (_currentChannel is null &&
                    _playback.CurrentChannel is null &&
                    !AnyPlaylistPlayerPlaying() &&
                    !AnyCartPlaying())
                {
                    _positionTimer.Stop();
                    _meterTimer.Stop();
                    ResetMeter();
                }

                // Reasignar el spectrum al canal que corresponda (o null).
                try { UpdateSpectrumChannel(); } catch { }
            }
        });
    }

    private void OnLocutionPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocutionPanelViewModel.IsPlaying)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (LocutionPanel.IsPlaying)
            {
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
            }
            else if (_currentChannel is null && _playback.CurrentChannel is null &&
                     !_currentPlaybackInfo.IsActive &&
                     !AnyPlaylistPlayerPlaying() && !AnyCartPlaying())
            {
                _positionTimer.Stop();
                _meterTimer.Stop();
                ResetMeter();
            }

            try { UpdateSpectrumChannel(); } catch { }
        });
    }

    private void InitializeEngine()
    {
        try
        {
            _engine.Initialize(deviceIndex: -1, sampleRate: 44100);
            ReloadBuses();

            // Aplicar el volumen actual del VM al engine (por si arranca en <1).
            try { _engine.GlobalVolume = Volume; } catch { }

            SetLocalizedStatus("Loc.Main.Status.EngineInitialized", Buses.Count);
            _logService.Info("Engine", $"Motor inicializado con {Buses.Count} bus(es).");

            // ── Inicializar RMVoice DESPUES del engine ────────────────────
            //    RMVoice crea streams en el device que le pasemos, pero
            //    necesita que BASS ya este inicializado (OwnsBass=false).
            InitializeLocution();

            // ── Recargar buses en el panel de locucion ────────────────────
            //    El panel se creo en el constructor ANTES de que el engine
            //    tuviera buses cargados. Ahora que ya estan, los refrescamos.
            try { LocutionPanel?.EnsureAvailableBusesLoaded(); } catch { }
        }
        catch (Exception ex)
        {
            LastError = string.Format(
                LocalizationService.GetString("Loc.Main.Error.EngineInitialization"),
                ex.Message);
            SetLocalizedStatus("Loc.Main.Status.EngineInitializationFailed");
            _logService.Error("Engine", "Fallo al inicializar", ex);
        }
    }

    /// -------------
    /// Devuelve TODOS los canales activos de la app (de todos los
    /// reproductores). Se usa para el ducking de locuciones disparadas
    /// desde el panel de clima/hora.
    /// -------------
    private IEnumerable<IAudioChannel> GetAllActiveChannels()
    {
        var list = new List<IAudioChannel>();

        // 1) Canal manual.
        try
        {
            if (_currentChannel is not null)
                list.Add(_currentChannel);
        }
        catch { }

        // 2) Canal del scheduler.
        try { list.AddRange(_playback.GetActiveChannels()); } catch { }

        // AutoDJ utiliza un player separado, pero comparte el engine y el aire.
        try { list.AddRange(_autoDjPlayer.GetActiveChannels()); } catch { }

        // 3) Playlist principal.
        try { list.AddRange(_playlistPlayer.GetActiveChannels()); } catch { }

        // 4) Canales de playlists adicionales (una por pestaña).
        // ── FASE B
        foreach (var p in _playlistPlayers)
        {
            try { list.AddRange(p.GetActiveChannels()); } catch { }
        }

        // 5) Canales de la botonera principal.
        try { list.AddRange(_cartPlayer.GetActiveChannels()); } catch { }

        // 6) Canales de las botoneras extras.
        foreach (var p in _extraCartPlayers)
        {
            try { list.AddRange(p.GetActiveChannels()); } catch { }
        }

        // 7) Playlist interna del watchdog de emergencia.
        try { list.AddRange(_emergency.GetActiveChannels()); } catch { }

        // 8) Audio de la demo de bienvenida, si está reproduciendo.
        try
        {
            var demoChannel = Demo?.GetCurrentChannel();
            if (demoChannel is not null) list.Add(demoChannel);
        }
        catch { }

        return list;
    }

    /// -------------
    /// Inicializa el servicio de locución de RMVoice. Es seguro llamarlo
    /// aunque RMVoice no esté disponible: en ese caso, deja el servicio
    /// en modo "no op" y sigue sin afectar a la app.
    /// -------------
    private void InitializeLocution()
    {
        try
        {
            _locution.Initialize();

            if (_locution.IsAvailable)
            {
                _logService.Info("Locution",
                    "RMVoice inicializado. Las locuciones estan disponibles.");
            }
            else
            {
                _logService.Warning("Locution",
                    "RMVoice no esta disponible. Las locuciones quedaran deshabilitadas.");
            }
        }
        catch (Exception ex)
        {
            _logService.Error("Locution",
                $"Error al inicializar RMVoice: {ex.Message}", ex);
        }
    }

    public void ReloadBuses()
    {
        var originallySelected = SelectedBus;

        _suppressBusSelectionHook = true;
        try
        {
            var engineBuses = _engine.Buses;

            for (int i = Buses.Count - 1; i >= 0; i--)
            {
                if (!engineBuses.Contains(Buses[i]))
                    Buses.RemoveAt(i);
            }

            foreach (var bus in engineBuses)
            {
                if (!Buses.Contains(bus))
                    Buses.Add(bus);
            }

            if (SelectedBus is null || !Buses.Contains(SelectedBus))
                SelectedBus = _engine.PrimaryBus;
        }
        finally
        {
            _suppressBusSelectionHook = false;
        }

        if (originallySelected is not null && !_engine.Buses.Contains(originallySelected))
        {
            if (HasFile)
            {
                ReleaseCurrentChannel();
                SetLocalizedStatus("Loc.Main.Status.PlaybackBusRemoved");
            }
        }

        OnPropertyChanged(nameof(HasBuses));

        // Avisar al servicio de routing que la lista cambio, asi los
        // selectores por modulo refrescan sus combos.
        _routing?.NotifyBusesChanged();
        // Refrescar la lista de buses del panel de locucion.
        try { LocutionPanel?.ReloadAvailableBuses(); } catch { }
    }

    public IReadOnlyList<(string Name, IReadOnlyList<string> Extensions)> GetFormatGroups()
    {
        var groups = new List<(string, IReadOnlyList<string>)>();

        var pluginExts = new HashSet<string>(
            _engine.LoadedPlugins
                   .Where(p => p.IsLoaded)
                   .SelectMany(p => p.Extensions),
            StringComparer.OrdinalIgnoreCase);

        var nativeExts = _engine.SupportedExtensions
            .Where(e => !pluginExts.Contains(e))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (nativeExts.Count > 0)
            groups.Add(("Nativos BASS", nativeExts));

        foreach (var plugin in _engine.LoadedPlugins
                                    .Where(p => p.IsLoaded && p.Extensions.Count > 0)
                                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            groups.Add((plugin.Name, plugin.Extensions));
        }

        return groups;
    }

    // ───── Handlers ──────────────────────────────────────────────────────

    private void OnSchedulerItemTriggered(object? sender, SchedulerEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SetLocalizedStatus("Loc.Main.Status.ScheduledTriggered", e.Item.DisplayTitle, e.SourceListName);
            _logService.Info("Scheduler", $"Item disparado: {e.Item.DisplayTitle}");

            foreach (var tab in TabHost.Tabs)
            {
                if (tab.Content is ScheduledListViewModel listVm)
                    listVm.MarkItemAsPlayed(e.Item.Id);
            }
        });
    }

    private void OnSchedulerStateReset(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var tab in TabHost.Tabs)
            {
                if (tab.Content is ScheduledListViewModel listVm)
                    listVm.ResetAllIsPlayed();
            }

            SetLocalizedStatus("Loc.Main.Status.TriggersReset");
            _logService.Info("Scheduler", "Estado de disparos reiniciado.");
        });
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateInfo state)
    {
        _currentPlaybackInfo = state;

        bool wasIdle = _lastPlaybackEngineKind == PlaybackStateKind.Idle ||
                       _lastPlaybackEngineKind == PlaybackStateKind.Error;
        bool isActive = state.Kind == PlaybackStateKind.Playing ||
                        state.Kind == PlaybackStateKind.Suspended;

        // ===== PHASE 2B: COORDINATOR MANEJA PAUSE/RESUME =====
        // El BusExclusivityCoordinator ahora se encarga de pausar playlists
        // cuando el scheduler arranca y de reanudarlas cuando termina.

        _lastPlaybackEngineKind = state.Kind;

        // Ahora sí, con el estado real de las playlists (resumidas o pausadas),
        // decidir los timers.
        switch (state.Kind)
        {
            case PlaybackStateKind.Playing:
            case PlaybackStateKind.Suspended:
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
                break;

            case PlaybackStateKind.Idle:
            case PlaybackStateKind.Error:
                if (_currentChannel is null &&
                    !AnyPlaylistPlayerPlaying() &&
                    !AnyCartPlaying())
                {
                    _positionTimer.Stop();
                    _meterTimer.Stop();
                    ResetMeter();
                }
                else
                {
                    // Algo quedó sonando (playlist resumida, cart, etc.):
                    // reasegurar timers.
                    if (!_positionTimer.IsEnabled) _positionTimer.Start();
                    if (!_meterTimer.IsEnabled) _meterTimer.Start();
                }
                break;
        }

        // Forzar update inmediato del spectrum (no esperar los 200ms del timer).
        try { UpdateSpectrumChannel(); } catch { }

        UpdateScheduledItemsPlayingState(state);
        UpdateTabLiveStates();
        SyncPlaybackState();
    }

    private void HandlePlaylistCoexistence(PlaylistPlayer? player, PlaylistPlayerState state)
    {
        // Logica adicional especifica de coexistencia. Por ahora, no hace nada:
        // el flujo principal esta en OnPlaybackStateChanged.
    }

    private void UpdateScheduledItemsPlayingState(PlaybackStateInfo state)
    {
        string? activeItemId = null;

        if (state.Kind == PlaybackStateKind.Playing ||
            state.Kind == PlaybackStateKind.Suspended)
        {
            activeItemId = FindItemIdByTitle(state.ItemTitle);
        }

        foreach (var tab in TabHost.Tabs)
        {
            if (tab.Content is ScheduledListViewModel listVm)
                listVm.SetCurrentlyPlaying(activeItemId);
        }
    }

    private string? FindItemIdByTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;

        foreach (var tab in TabHost.Tabs)
        {
            if (tab.Content is ScheduledListViewModel listVm)
            {
                foreach (var item in listVm.Items)
                {
                    if (item.DisplayTitle == title)
                        return item.Source.Id;
                }
            }
        }
        return null;
    }

    private void OnPlaylistPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        _currentPlaylistInfo = state;
    
        switch (state.Status)
        {
            case PlaylistPlayerStatus.Playing:
            case PlaylistPlayerStatus.Paused:
            case PlaylistPlayerStatus.PausedByScheduler:
            case PlaylistPlayerStatus.Delaying:
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
                break;

            case PlaylistPlayerStatus.Stopped:
            case PlaylistPlayerStatus.Error:
                if (_currentChannel is null && _playback.CurrentChannel is null)
                {
                    _positionTimer.Stop();
                    _meterTimer.Stop();
                    ResetMeter();
                }
                break;
        }

        // La playlist puede reanudarse después de que el Scheduler haya
        // pasado a Idle y detenido los timers. Su evento Playing debe
        // reactivarlos y volver a enlazar inmediatamente el canal del VU y
        // del Spectrum.
        if (state.Status == PlaylistPlayerStatus.Playing)
        {
            if (!_positionTimer.IsEnabled) _positionTimer.Start();
            if (!_meterTimer.IsEnabled) _meterTimer.Start();
        }

        SyncPlaybackState();
        try { UpdateSpectrumChannel(); } catch { }
    }

    private void OnCartButtonStateChanged(object? sender, CartButtonPlaybackEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool anythingPlaying = AnyCartPlaying();

            if (anythingPlaying)
            {
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
            }
            else
            {
                // Si no hay nada sonando en NINGUN modulo, parar timers.
                if (_currentChannel is null &&
                    _playback.CurrentChannel is null &&
                    !AnyPlaylistPlayerPlaying())
                {
                    _positionTimer.Stop();
                    _meterTimer.Stop();
                    ResetMeter();
                }
            }

            // ===== FIX INDICADOR =====
            // Forzar la actualizacion de tabs INMEDIATAMENTE para que el
            // indicador cambie rojo→naranja (o apagado) sin esperar los
            // 500ms del RefreshTopPanel.
            try { UpdateTabLiveStates(); } catch { }

            // Sincronizar el estado del status bar inmediatamente.
            SyncPlaybackState();
        });
    }

    private void OnFileActivated(object? sender, AudioFileViewModel file)
    {
        if (file is null) return;

        var selectedTab = TabHost.SelectedTab;
        if (selectedTab is null)
        {
            SetLocalizedStatus("Loc.Main.Status.NoActiveTab");
            return;
        }

        try
        {
            switch (selectedTab.Content)
            {
                case PlaylistEditorViewModel playlist:
                    playlist.AddTrackFromFile(file.FullPath);
                    SetLocalizedStatus("Loc.Main.Status.AddedToPlaylist", file.FileName);
                    _logService.Info("FileBrowser", $"Agregado a playlist: {file.FileName}");
                    break;

                case EncadenadoresEditorViewModel encadenadores:
                    encadenadores.AddFile(file.FullPath);
                    SetLocalizedStatus("Loc.Main.Status.AddedToChainers", file.FileName);
                    _logService.Info("FILE MANAGER", $"Agregado a Encadenadores: {file.FileName}");
                    break;

                case ScheduledListViewModel scheduled:
                    scheduled.AddFileAsItem(file.FullPath);
                    SetLocalizedStatus("Loc.Main.Status.AddedToScheduled", file.FileName);
                    _logService.Info("FileBrowser", $"Agregado a programados: {file.FileName}");
                    break;

                case TandaEditorViewModel tanda:
                    tanda.AddTrackFromFile(file.FullPath);
                    SetLocalizedStatus("Loc.Main.Status.AddedToSequence", file.FileName);
                    _logService.Info("FileBrowser", $"Agregado a tanda: {file.FileName}");
                    break;

                default:
                    SetLocalizedStatus("Loc.Main.Status.TabDoesNotAcceptFiles", selectedTab.Title);
                    _logService.Warning("FILE MANAGER",
                        $"No se pudo agregar '{file.FileName}': la pestaña '{selectedTab.Title}' no admite archivos.");
                    break;
            }
        }
        catch (Exception ex)
        {
            SetLocalizedStatus("Loc.Main.Status.AddFileError", ex.Message);
            _logService.Error("FileBrowser", "Error al agregar archivo", ex);
        }
    }

    /// -------------
    /// Crea un CartBoard extra con su propio CartBoardPlayer independiente.
    /// El nuevo cart arranca vacio (sin archivo). Si el usuario lo quiere
    /// persistir, tiene que guardarlo con "Guardar como...".
    /// -------------
    private CartBoardViewModel CreateExtraCartBoard()
    {
        var player = new CartBoardPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _coordinator);

        player.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "CART", logDetails);

        // ── FASE C: bus propio del cart. Default: el del modulo Cart.
        var initialBus = GetCartDefaultBus();
        player.SetBus(initialBus);

        player.IsCrossfadeEnabled = Fade.CartCrossfadeEnabled;
        player.CrossfadeDurationMs = (int)Math.Round(Fade.CartCrossfadeSeconds * 1000.0);

        player.ButtonStateChanged += OnCartButtonStateChanged;
        player.CrossfadeStateChanged += (_, _) =>
        {
            try { _cartPlayer.IsCrossfadeEnabled = player.IsCrossfadeEnabled; } catch { }
        };

        _extraCartPlayers.Add(player);

        var board = new RadioMaker.Core.Scheduling.CartBoard();
        board.Normalize();

        var vm = new CartBoardViewModel(board, null, player, defaultBusProvider: GetCartDefaultBus);

        var selector = new SimpleBusSelectorViewModel(
            _engine,
            initialBus: initialBus,
            onBusChanged: bus => { try { player.SetBus(bus); } catch { } });

        vm.AttachBusSelector(selector);

        return vm;
    }

    /// -------------
    /// Devuelve el bus del modulo Cart (el default de las botoneras).
    /// Si el routing no esta disponible, cae al primario.
    /// -------------
    private IAudioBus? GetCartDefaultBus()
    {
        try
        {
            if (_routing is not null)
                return _routing.GetEffectiveBus(AudioModuleKind.Cart);
        }
        catch { }

        try { return _engine.PrimaryBus; } catch { return null; }
    }

    /// -------------
    /// Crea un nuevo editor de playlist con su PROPIO PlaylistPlayer y su
    /// PROPIO SimpleBusSelector. Se llama cada vez que se abre una nueva
    /// pestaña de playlist.
    ///
    /// El bus inicial es el del modulo Scheduler (el "aire"). Si la
    /// playlist tiene un OutputBusId persistido, se restaura al cargar
    /// el archivo. (FASE B)
    /// -------------
    private PlaylistEditorViewModel CreatePlaylistEditorForTab()
    {
        // 1) Crear un player nuevo.
        var player = new PlaylistPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _locution,
            _coordinator,
            _encadenadoresRegistry);

        player.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "PLAYLIST", logDetails);

        player.PrewarmFile = path => _configurator.Prewarm(path);

        // ===== FIX FASE B =====
        // Aplicar el InterruptionBehavior persistido al player nuevo.
        // Antes solo se aplicaba al player legacy, y los players por
        // pestaña seguian con el default (Continue).
        try
        {
            var savedBehavior = InterruptionBehaviorSettingsStore.Load().Behavior;
            player.SetInterruptionBehavior(savedBehavior);
        }
        catch { }

        // Aplicar crossfade configurado.
        try
        {
            double sec = Fade.CartCrossfadeEnabled ? Fade.CartCrossfadeSeconds : 0;
            player.SetCrossfadeSeconds(sec);
        }
        catch { }

        // 2) Resolver el bus default (el del Scheduler).
        var defaultBus = GetSchedulerBus();

        // 3) Crear el selector simple. El callback setea el bus en el player.
        var selector = new SimpleBusSelectorViewModel(
            _engine,
            initialBus: defaultBus,
            onBusChanged: bus =>
            {
                try { player.SetBus(bus); } catch { }
            });

        // 4) Suscribir el state del player para que el ViewModel principal
        //    reaccione a los eventos (ducking de ALTA, actualizacion de UI, etc).
        player.StateChanged += OnAnyPlaylistPlayerStateChanged;
        player.SessionStarted += OnAnyPlaylistSessionStarted;
        player.SessionEnded += OnAnyPlaylistSessionEnded;

        // 5) Registrar el player.
        _playlistPlayers.Add(player);

        // 6) Crear el VM del editor y conectarle el selector.
        var vm = new PlaylistEditorViewModel(
            new Playlist(),
            player,
            schedulerBusProvider: GetSchedulerBus);

        vm.AttachBusSelector(selector);

        return vm;
    }

    /// -------------
    /// Libera los recursos de un editor de playlist al cerrar su pestaña.
    /// -------------
    private void DisposePlaylistEditor(PlaylistEditorViewModel vm)
    {
        if (vm is null) return;

        try
        {
            // Propiedad publica: reemplaza reflection sobre _player.
            var player = vm.Player;
            if (player is not null)
            {
                try { player.StateChanged -= OnAnyPlaylistPlayerStateChanged; } catch { }
                try { player.SessionStarted -= OnAnyPlaylistSessionStarted; } catch { }
                try { player.SessionEnded -= OnAnyPlaylistSessionEnded; } catch { }

                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }

                _playlistPlayers.Remove(player);
            }
        }
        catch { }
    }

    /// -------------
    /// Devuelve el bus del modulo Scheduler (el "aire"). Si no hay
    /// routing disponible, cae al primario.
    /// -------------
    private IAudioBus? GetSchedulerBus()
    {
        try
        {
            if (_routing is not null)
                return _routing.GetEffectiveBus(AudioModuleKind.Scheduler);
        }
        catch { }

        try { return _engine.PrimaryBus; } catch { return null; }
    }

    /// -------------
    /// Handler comun a todos los players de playlist. Se usa para
    /// coordinar la coexistencia con el Scheduler.
    /// -------------
    private void OnAnyPlaylistPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HandlePlaylistCoexistence(sender as PlaylistPlayer, state);

            // La transición Scheduler → Idle puede apagar ambos timers
            // justo antes de que llegue la notificación de reanudación de
            // la playlist. Reactivarlos desde su estado Playing evita que
            // el VU quede congelado y fuerza la reconexión del Spectrum.
            if (state.Status == PlaylistPlayerStatus.Playing)
            {
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
            }

            try { UpdateSpectrumChannel(); } catch { }
            SyncPlaybackState();
        });
        UpdateTabLiveStates();
    }

    private void OnAnyPlaylistSessionStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_positionTimer.IsEnabled) _positionTimer.Start();
            if (!_meterTimer.IsEnabled) _meterTimer.Start();

            // ===== FIX EXCLUSIVIDAD =====
            // Forzar el cambio de canal del spectrum inmediatamente, sin
            // esperar los 200ms del _positionTimer. El usuario ve el cambio
            // en el mismo frame en que arranca la playlist.
            try { UpdateSpectrumChannel(); } catch { }
        });
    }

    private void OnAnyPlaylistSessionEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Si no hay otro reproductor activo, detener timers.
            if (_currentChannel is null &&
                _playback.CurrentChannel is null &&
                !AnyPlaylistPlayerPlaying() &&
                !AnyCartPlaying())
            {
                _positionTimer.Stop();
                _meterTimer.Stop();
                ResetMeter();
            }

            // Sincronizar el estado del status bar.
            SyncPlaybackState();
        });
    }

    private bool AnyPlaylistPlayerPlaying()
    {
        try { if (_autoDjPlayer.IsPlayingNow) return true; } catch { }
        foreach (var p in _playlistPlayers)
        {
            try { if (p.IsPlayingNow) return true; } catch { }
        }
        return false;
    }

    /// -------------
    /// Libera los recursos de un CartBoard extra al cerrar su pestaña.
    /// -------------
    private void DisposeExtraCartBoard(CartBoardViewModel cartVm)
    {
        try
        {
            // Encontrar el player asociado a este VM.
            // El CartBoardViewModel no expone el player publicamente, pero
            // el player es el mismo que se registro en CreateExtraCartBoard.
            // Como no podemos identificar cual es, simplemente detenemos
            // todos los canales activos del cart antes de liberarlo.
            cartVm.StopAllCommand?.Execute(null);
        }
        catch { }

        // Nota: los players extras se disponen en Dispose() del MainWindowViewModel.
        // Esta limpieza individual es best-effort por ahora.
    }

    /// -------------
    /// Detiene toda la reproduccion en curso: botones del cart, playlist
    /// activa, scheduler y canal manual. Se invoca desde el menu
    /// "Reproduccion -> Detener todo".
    /// -------------
    public void PrepareAutoDjPlaylist(AutoDjClock clock, IReadOnlyList<AutoDjClockTrack> tracks)
    {
        if (clock is null || tracks is null || tracks.Count == 0) return;
        var playlist = CreateAutoDjPlaylist(clock, tracks);
        _autoDjPreparedPlaylist = playlist;

        string status;
        try
        {
            PlaylistStore.Save(playlist, AutoDjTemporaryPlaylistPath);
            status = $"Vista previa AutoDJ guardada temporalmente ({tracks.Count} pistas).";
        }
        catch (Exception ex)
        {
            status = $"Vista previa AutoDJ lista ({tracks.Count} pistas), pero no se pudo guardar temporalmente: {ex.Message}";
            _logService.Warning("AUTODJ", status);
        }

        NotifyAutoDjButtonChanged();
        if (_autoDjPlayer.IsPlayingNow) return;
        ShowAutoDjPlaylist(playlist, status);
        RefreshNowPlayingPanel();
        RefreshNextPlayingPanel();
    }

    private Playlist CreateAutoDjPlaylist(AutoDjClock clock, IReadOnlyList<AutoDjClockTrack> tracks)
    {
        var airBus = GetSchedulerBus();
        return new Playlist
        {
            Name = "AUTODJ",
            Mode = PlaylistMode.Loop,
            OutputBusId = airBus?.Id ?? string.Empty,
            Tracks = tracks.Select(item => new PlaylistTrack
            {
                FilePath = item.Candidate.FilePath,
                Title = !string.IsNullOrWhiteSpace(item.Candidate.Tags.DisplayTitle)
                    ? item.Candidate.Tags.DisplayTitle
                    : item.Candidate.DisplayName,
                DurationSeconds = item.Candidate.DurationSeconds
            }).ToList()
        };
    }

    private void ShowAutoDjPlaylist(Playlist playlist, string status)
    {
        _autoDjEditorVm ??= new PlaylistEditorViewModel(
            playlist, _autoDjPlayer, schedulerBusProvider: GetSchedulerBus, stopPlaybackAction: StopAutoDj);
        _autoDjEditorVm.SetStopPlaybackAction(StopAutoDj);
        _autoDjEditorVm.SetAutoDjEnabledProvider(() => IsAutoDjEnabled);
        _autoDjEditorVm.SetAutoDjPlaylist(playlist, status);
        TabHost.ShowAutoDjPlaylist(_autoDjEditorVm);
    }

    [RelayCommand]
    private void ToggleAutoDj()
    {
        if (IsAutoDjEnabled)
        {
            StopAutoDj();
            return;
        }

        if (_autoDjPreparedPlaylist is not { Tracks.Count: > 0 })
        {
            var saved = PlaylistStore.Load(AutoDjTemporaryPlaylistPath);
            if (saved is { Tracks.Count: > 0 })
            {
                _autoDjPreparedPlaylist = saved;
                ShowAutoDjPlaylist(saved, "Lista AutoDJ recuperada del guardado temporal.");
                NotifyAutoDjButtonChanged();
            }
        }

        if (_autoDjPreparedPlaylist is not { Tracks.Count: > 0 })
        {
            OpenAutoDjPlannerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        StartPreparedAutoDj("lista AutoDJ", _autoDjPreparedPlaylist.Tracks.Count);
    }

    private bool StartPreparedAutoDj(string sourceName, int trackCount)
    {
        if (_autoDjPreparedPlaylist is not { Tracks.Count: > 0 }) return false;
        var airBus = GetSchedulerBus();
        if (airBus is null)
        {
            SetAutoDjStatus("Loc.Main.AutoDj.Status.NoAirBus");
            SetLocalizedStatus("Loc.Main.AutoDj.Status.NoAirBus");
            return false;
        }

        _autoDjPlaylist = _autoDjPreparedPlaylist;
        _autoDjPlaylist.OutputBusId = airBus.Id;
        ShowAutoDjPlaylist(_autoDjPlaylist, "AutoDJ habilitado; esperando un punto seguro para tomar el aire.");
        IsAutoDjEnabled = true;
        SetAutoDjStatus("Loc.Main.AutoDj.Status.WaitingSafePoint");
        _autoDjNextStartAttemptUtc = DateTime.MinValue;

        if (_autoDjPlayer.IsPlayingNow)
        {
            _autoDjPlayer.StopAfterCurrentTrack();
            SetAutoDjStatus("Loc.Main.AutoDj.Status.NewClockWaitingForTrackEnd");
        }
        else
        {
            _autoDjPlayer.SetBus(airBus);
            RequestActivePlaylistsToYield(airBus);
        }

        _autoDjTimer.Start();
        _logService.Info("AUTODJ", $"'{sourceName}' habilitado con {trackCount} pista(s). Se esperará el final de la pista actual y el bus libre.");
        TryStartAutoDjWhenAirIsFree();
        return true;
    }

    public Task<bool> TakeAutoDjAirAsync(AutoDjClock clock, IReadOnlyList<AutoDjClockTrack> tracks)
    {
        if (clock is null || tracks is null || tracks.Count == 0)
        {
            SetAutoDjStatus("Loc.Main.AutoDj.Status.ClockHasNoPlayableTracks");
            _logService.Warning("AUTODJ", AutoDjStatusText);
            return Task.FromResult(false);
        }

        if (_autoDjPreparedPlaylist is not { Tracks.Count: > 0 } prepared ||
            !prepared.Tracks.Select(t => t.FilePath).SequenceEqual(
                tracks.Select(t => t.Candidate.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            _autoDjPreparedPlaylist = CreateAutoDjPlaylist(clock, tracks);
            try { PlaylistStore.Save(_autoDjPreparedPlaylist, AutoDjTemporaryPlaylistPath); }
            catch (Exception ex) { _logService.Warning("AUTODJ", $"No se pudo actualizar el guardado temporal: {ex.Message}"); }
        }

        return Task.FromResult(StartPreparedAutoDj($"reloj '{clock.Name}'", tracks.Count));
    }

    public void StopAutoDj()
    {
        if (!IsAutoDjEnabled && _autoDjPlaylist is null) return;
        IsAutoDjEnabled = false;
        _autoDjTimer.Stop();
        try { _autoDjPlayer.Stop(); } catch { }
        try { _playlistPlayer.CancelStopAfterCurrentTrack(); } catch { }
        foreach (var player in _playlistPlayers)
        {
            try { player.CancelStopAfterCurrentTrack(); } catch { }
        }
        _autoDjPlaylist = null;
        SetAutoDjStatus("Loc.Main.AutoDj.Status.Disabled");
        NotifyAutoDjButtonChanged();
        _logService.Info("AUTODJ", "AutoDJ desactivado por el operador.");
    }

    private void TryStartAutoDjWhenAirIsFree()
    {
        if (!IsAutoDjEnabled || _autoDjPlaylist is null) return;
        var airBus = GetSchedulerBus();
        if (airBus is null) return;

        if (_autoDjPlayer.IsPlayingNow)
        {
            if (!string.Equals(_autoDjPlayer.TargetBus?.Id, airBus.Id, StringComparison.OrdinalIgnoreCase))
            {
                _autoDjPlayer.StopAfterCurrentTrack();
                SetAutoDjStatus("Loc.Main.AutoDj.Status.BusChangeWaitingForTrackEnd");
            }
            return;
        }
        if (_autoDjPlayer.CurrentStatus is PlaylistPlayerStatus.Paused or PlaylistPlayerStatus.PausedByScheduler) return;

        if (!string.Equals(_autoDjPlayer.TargetBus?.Id, airBus.Id, StringComparison.OrdinalIgnoreCase))
            _autoDjPlayer.SetBus(airBus);

        RequestActivePlaylistsToYield(airBus);

        var currentOwner = _coordinator.GetCurrentOwnerId(airBus.Id);
        if (!string.IsNullOrEmpty(currentOwner))
        {
            SetAutoDjStatus("Loc.Main.AutoDj.Status.WaitingForAirBus");
            return;
        }

        if (DateTime.UtcNow < _autoDjNextStartAttemptUtc) return;
        _autoDjNextStartAttemptUtc = DateTime.UtcNow.AddSeconds(5);
        if (_autoDjEditorVm is null)
        {
            _autoDjEditorVm = new PlaylistEditorViewModel(
                _autoDjPlaylist, _autoDjPlayer, schedulerBusProvider: GetSchedulerBus, stopPlaybackAction: StopAutoDj);
        }
        _autoDjEditorVm.SetStopPlaybackAction(StopAutoDj);
        _autoDjEditorVm.SetAutoDjEnabledProvider(() => IsAutoDjEnabled);
        _autoDjEditorVm.SetAutoDjPlaylist(_autoDjPlaylist, LocalizationService.GetString("Loc.Main.AutoDj.Status.ReadyToAir"));
        TabHost.ShowAutoDjPlaylist(_autoDjEditorVm);
        _autoDjPlayer.Play(_autoDjPlaylist);
        if (_autoDjPlayer.IsPlayingNow)
        {
            SetAutoDjStatus("Loc.Main.AutoDj.Status.OnAir", _autoDjPlaylist.Name);
            _logService.Info("AUTODJ", $"Tomó el aire en '{airBus.DisplayName}'.");
        }
        else
        {
            SetAutoDjStatus("Loc.Main.AutoDj.Status.StartFailedRetrying");
        }
    }

    private void RequestActivePlaylistsToYield(IAudioBus airBus)
    {
        IEnumerable<PlaylistPlayer> players = new[] { _playlistPlayer }.Concat(_playlistPlayers);
        foreach (var player in players)
        {
            try
            {
                var playerBus = player.TargetBus ?? _engine.PrimaryBus;
                if (player.IsPlayingNow &&
                    string.Equals(playerBus.Id, airBus.Id, StringComparison.OrdinalIgnoreCase))
                    player.StopAfterCurrentTrack();
            }
            catch { }
        }
    }

    private void OnAutoDjPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsAutoDjEnabled)
            {
                switch (state.Status)
                {
                    case PlaylistPlayerStatus.Playing:
                        SetAutoDjStatus("Loc.Main.AutoDj.Status.OnAir", _autoDjPlaylist?.Name ?? "AutoDJ");
                        break;
                    case PlaylistPlayerStatus.PausedByScheduler:
                        SetAutoDjStatus("Loc.Main.AutoDj.Status.PausedByScheduled");
                        break;
                    case PlaylistPlayerStatus.Paused:
                        SetAutoDjStatus("Loc.Main.AutoDj.Status.Paused");
                        break;
                    default:
                        SetAutoDjStatus("Loc.Main.AutoDj.Status.WaitingSafePoint");
                        break;
                }
            }

            if (state.Status == PlaylistPlayerStatus.Playing)
            {
                if (!_positionTimer.IsEnabled) _positionTimer.Start();
                if (!_meterTimer.IsEnabled) _meterTimer.Start();
            }
            else if (state.Status is PlaylistPlayerStatus.Stopped or PlaylistPlayerStatus.Error)
            {
                if (HasPlaybackWithoutAutoDj())
                {
                    if (!_meterTimer.IsEnabled) _meterTimer.Start();
                    RefreshMeter();
                }
                else
                {
                    _meterTimer.Stop();
                    ResetMeter();
                }
            }

            try { UpdateSpectrumChannel(); } catch { }
            try { RefreshNowPlayingPanel(); } catch { }
            try { RefreshNextPlayingPanel(); } catch { }
            OnPropertyChanged(nameof(NowPlayingSpectrum));
            SyncPlaybackState();
        });
    }

    private bool HasPlaybackWithoutAutoDj()
    {
        if (_locution.IsPlaying) return true;

        try { if (_playlistPlayer.IsPlayingNow) return true; } catch { }
        foreach (var player in _playlistPlayers)
        {
            try { if (player.IsPlayingNow) return true; } catch { }
        }

        foreach (var channel in GetAllActiveChannels())
        {
            if (ReferenceEquals(channel, _autoDjPlayer.CurrentChannel)) continue;
            try
            {
                if (channel.State is PlaybackState.Playing or PlaybackState.Stalled)
                    return true;
            }
            catch { }
        }

        return false;
    }

    partial void OnIsAutoDjEnabledChanged(bool value)
    {
        _autoDjEditorVm?.RefreshAutoDjState();
        NotifyAutoDjButtonChanged();
    }

    public void StopAllPlayback()
    {
        StopAutoDj();
        try { _cartPlayer.StopAll(); } catch { }
        try { _playlistPlayer.Stop(); } catch { }
        try { _playback.StopAll(); } catch { }

        // Canal manual (boton "Examinar").
        try { _currentChannel?.Stop(); } catch { }

        // Detener los timers de UI.
        _positionTimer.Stop();
        _meterTimer.Stop();
        ResetMeter();

        SetLocalizedStatus("Loc.Main.Status.StopAll");
        _logService.Info("APP", "Detener todo ejecutado (cart + playlist + scheduler + manual).");

        RefreshCommandStates();
    }

    /// -------------
    /// Handler comun para los cambios de configuracion de EQ, Trim y
    /// Normalizacion. Reune todos los canales activos de los 4 reproductores
    /// (manual, scheduler, playlist, cart) y les reaplica la configuracion.
    ///
    /// Esto resuelve el bug donde cambiar el EQ no tenia efecto sobre audio
    /// que estaba sonando desde el cart, el scheduler o una playlist.
    /// -------------
    private void OnProcessingSettingsChanged(object? sender, EventArgs e)
    {
        var settingsMessage = sender switch
        {
            EqualizerPanelViewModel eq =>
                $"EQ: Enabled={eq.IsEnabled}; Preset={eq.SelectedPreset}; Bands=" +
                string.Join(", ", eq.Bands.Select(b => $"{b.Label}:{b.Gain:+0.0;-0.0;0.0}dB")),
            SilencePanelViewModel silence =>
                $"Trim: Enabled={silence.AutoTrimEnabled}; Threshold={silence.ThresholdDb:F1}dB; " +
                $"Block={silence.BlockMs}ms; MinSilence={silence.MinSilenceMs}ms.",
            NormalizationPanelViewModel norm =>
                $"Normalization: Enabled={norm.IsEnabled}; Mode={norm.Mode}; Target={norm.TargetDb:F1}dB.",
            _ => null
        };
        if (settingsMessage is not null)
            _logService.Info("SETTINGS AUDIO", settingsMessage);

        try
        {
            // Usar el inventario común: incluye todas las playlists, carts,
            // canales del scheduler y reproducciones auxiliares.
            _configurator.ReapplyToAll(GetAllActiveChannels());

            // Reaplicar configuración puede reemplazar la ganancia de
            // normalización; restaurar el offset propio de cada botón cart.
            try { _cartPlayer.ReapplyLiveVolumeOffsets(_configurator.IsAnalysisPending); } catch { }
            foreach (var cart in _extraCartPlayers)
            {
                try { cart.ReapplyLiveVolumeOffsets(_configurator.IsAnalysisPending); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logService.Error("Config", "Error al reaplicar configuracion", ex);
        }
    }

    private void ApplyCrossfadeToPlaylistPlayers()
    {
        double sec = Fade.CartCrossfadeEnabled ? Fade.CartCrossfadeSeconds : 0;
        try { _playlistPlayer?.SetCrossfadeSeconds(sec); } catch { }
        try { _autoDjPlayer?.SetCrossfadeSeconds(sec); } catch { }
        foreach (var p in _playlistPlayers)
        {
            try { p.SetCrossfadeSeconds(sec); } catch { }
        }
    }

    /// -------------
    /// Aplica el estado y la duracion del crossfade al cart player
    /// principal y a todos los cart players extras (botoneras adicionales).
    /// Se llama al arrancar y cada vez que el usuario cambia el valor en
    /// el panel de configuracion.
    /// -------------
    private void ApplyCrossfadeToCartPlayers()
    {
        bool enabled = Fade.CartCrossfadeEnabled;
        int ms = (int)Math.Round(Fade.CartCrossfadeSeconds * 1000.0);

        try { _cartPlayer.IsCrossfadeEnabled = enabled; } catch { }
        try { _cartPlayer.CrossfadeDurationMs = ms; } catch { }

        foreach (var p in _extraCartPlayers)
        {
            try { p.IsCrossfadeEnabled = enabled; } catch { }
            try { p.CrossfadeDurationMs = ms; } catch { }
        }
    }

    /// -------------
    /// Propaga el comportamiento de interrupcion a TODOS los players de
    /// playlist (legacy + FASE B). Se llama cuando el usuario cambia la
    /// setting en el panel de Scheduler.
    /// -------------
    private void ApplyInterruptionBehaviorToAllPlaylistPlayers(PlaylistInterruptionBehavior behavior)
    {
        // Legacy player
        try { _playlistPlayer.SetInterruptionBehavior(behavior); } catch { }

        // Players FASE B (uno por pestaña)
        foreach (var p in _playlistPlayers)
        {
            try { p.SetInterruptionBehavior(behavior); } catch { }
        }
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    /// -------------
    /// Alterna el tema entre claro y oscuro.
    ///
    /// Reglas:
    ///   - Si esta en Dark explicito -> pasa a Light explicito.
    ///   - Si esta en Light explicito -> pasa a Dark explicito.
    ///   - Si esta en System -> pasa al opuesto del tema efectivo actual
    ///     (si el SO esta en oscuro, pasa a Light explicito).
    /// -------------
    [RelayCommand]
    private void ToggleTheme()
    {
        var target = ThemeService.IsDarkVariant
            ? ThemePreference.Light
            : ThemePreference.Dark;

        ThemeService.SetPreference(target);
    }

    [RelayCommand]
    private void StopEmergency()
    {
        // Reutiliza la misma lógica que el botón de la toolbar: detiene,
        // desarma y persiste. No pide confirmación porque el botón "DETENER
        // EMERGENCIA" del banner ya es una acción explícita del operador.
        ApplyEmergencyStop();
    }

    [RelayCommand] private void Play()
    {
        try
        {
            if (_currentChannel is null) return;
            _currentChannel.Play();
            _positionTimer.Start();
            _meterTimer.Start();
            SetLocalizedStatus("Loc.Main.Status.Playing");
            _logService.Info("Player", "Reproduciendo manual.");
            LastError = string.Empty;
            SyncPlaybackState();
            RefreshCommandStates();
        }
        catch (Exception ex) { LastError = ex.Message; _logService.Error("Player", "Error en Play", ex); }
    }

    [RelayCommand] private void Pause()
    {
        try
        {
            _currentChannel?.Pause();
            _positionTimer.Stop();
            _meterTimer.Stop();
            SetLocalizedStatus("Loc.Main.Status.Paused");
            _logService.Info("PLAYER MANUAL", $"Pausa solicitada: {Path.GetFileName(CurrentFilePath)}.");
            SyncPlaybackState();
            RefreshCommandStates();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logService.Error("PLAYER MANUAL", "Error al pausar", ex);
        }
    }

    [RelayCommand] private void Stop()
    {
        try
        {
            _currentChannel?.Stop();
            _positionTimer.Stop();
            _meterTimer.Stop();
            Position = TimeSpan.Zero;
            ResetMeter();
            SetLocalizedStatus("Loc.Main.Status.Stopped");
            _logService.Info("PLAYER MANUAL", $"Detención solicitada: {Path.GetFileName(CurrentFilePath)}.");
            SyncPlaybackState();
            RefreshCommandStates();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logService.Error("PLAYER MANUAL", "Error al detener", ex);
        }
    }

    public void LoadFile(string filePath)
    {
        try
        {
            if (SelectedBus is null) { LastError = LocalizationService.GetString("Loc.Main.Error.NoOutputBus"); return; }

            ReleaseCurrentChannel();

            _currentChannel = SelectedBus.LoadFile(filePath);
            _currentChannel.Ended += OnChannelEnded;
            _currentChannel.StateChanged += OnChannelStateChanged;
            _currentChannel.SetVolume(Volume);

            _configurator.ConfigureChannel(_currentChannel, filePath, "MANUAL", true);

            Equalizer.AttachToChannel(_currentChannel);
            Silence.AttachToChannel(_currentChannel);
            Normalization.AttachToChannel(_currentChannel);
            Fade.AttachToChannel(_currentChannel);

            //_configurator.Prewarm(filePath);  // duplicado quitar

            CurrentFilePath = filePath;
            Duration = _currentChannel.Duration;
            Position = TimeSpan.Zero;
            PlaybackState = PlaybackState.Stopped;
            ResetMeter();
            SetLocalizedStatus("Loc.Main.Status.FileLoaded", Path.GetFileName(filePath));
            _logService.Info("Player", $"Cargado: {Path.GetFileName(filePath)}");
            LastError = string.Empty;
            SyncPlaybackState();
            RefreshCommandStates();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            CurrentFilePath = string.Empty;
            Duration = TimeSpan.Zero;
            _logService.Error("Player", "Error al cargar archivo", ex);
            RefreshCommandStates();
        }
    }

    private void ReleaseCurrentChannel()
    {
        _positionTimer.Stop();
        _meterTimer.Stop();
        ResetMeter();

        if (_currentChannel is null) return;

        try
        {
            _currentChannel.Ended -= OnChannelEnded;
            _currentChannel.StateChanged -= OnChannelStateChanged;
            _currentChannel.Dispose();
        }
        catch { }
        finally
        {
            _currentChannel = null;
            Equalizer.AttachToChannel(null);
            Silence.AttachToChannel(null);
            Normalization.AttachToChannel(null);
            Fade.AttachToChannel(null);
            CurrentFilePath = string.Empty;
            Duration = TimeSpan.Zero;
            Position = TimeSpan.Zero;
            PlaybackState = PlaybackState.Stopped;
        }
    }

    private void OnChannelEnded(object? sender, ChannelEndedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _positionTimer.Stop();
            _meterTimer.Stop();
            if (e.WasStoppedByUser)
            {
                Position = TimeSpan.Zero;
                PlaybackState = PlaybackState.Stopped;
                SetLocalizedStatus("Loc.Main.Status.StoppedByUser");
                _logService.Info("PLAYER MANUAL", $"Detenido: {Path.GetFileName(CurrentFilePath)}.");
            }
            else
            {
                Position = Duration;
                PlaybackState = PlaybackState.Ended;
                SetLocalizedStatus("Loc.Main.Status.PlaybackFinished");
                _logService.Info("PLAYER MANUAL", $"Reproducción finalizada: {Path.GetFileName(CurrentFilePath)}.");
            }
            SyncPlaybackState();
            RefreshCommandStates();
        });
    }

    private void OnChannelStateChanged(object? sender, PlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SyncPlaybackState();
            RefreshCommandStates();
        });
    }

    // ═════════════════════════════════════════════════════════════════════
    // Panel superior: LED de tiempo + fecha/hora
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Actualiza el LED de tiempo (transcurrido / restante) y el LED de
    /// fecha/hora. Se ejecuta cada 500ms sin importar el estado de la app.
    ///
    /// Prioridad para elegir el canal del que se muestra el tiempo:
    ///   1. Scheduler (programados / tandas)
    ///   2. Playlist
    ///   3. Cart
    ///   4. Canal manual (boton "Examinar")
    ///
    /// Se elige el primero que este REPRODUCIENDO (no pausado, no detenido).
    /// -------------
    private void RefreshTopPanel()
    {
        // 1) Fecha y hora actuales.
        var now = DateTime.Now;
        DateText = now.ToString("dd-MM-yy");
        TimeText = now.ToString("HH:mm:ss");

        // 2) Elegir canal por prioridad.
        var active = GetPriorityChannel();
        bool anyPlaying = active is not null;

        IsAnyAudioPlaying = anyPlaying;

        // 3) Actualizar el LED de tiempo.
        if (anyPlaying && active is not null)
        {
            try
            {
                var pos = active.Position;
                var dur = active.Duration;
                var remaining = dur - pos;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;

                ElapsedText = FormatLedTime(pos);
                RemainingText = "-" + FormatLedTime(remaining);
            }
            catch
            {
                ElapsedText = "00:00:00";
                RemainingText = "-00:00:00";
            }
        }
        else
        {
            ElapsedText = "00:00:00";
            RemainingText = "-00:00:00";
        }

        // 4) Panel "En reproduccion".
        RefreshNowPlayingPanel();
        OnPropertyChanged(nameof(NowPlayingSpectrum));

        // 5) Panel "Siguiente".
        RefreshNextPlayingPanel();
        RefreshNextCountdown();

        // 6) Actualizar el indicador de "al aire" en las pestañas.
        UpdateTabLiveStates();

        // 7) Sincronizar el estado de reproduccion del status bar con el
        // canal activo. Sin esto, cuando un cart/playlist/scheduler termina
        // sin pasar por el canal manual, el campo PlaybackState quedaba
        // pegado en "Playing" para siempre.
        SyncPlaybackState();
    }

    private void AdvanceTip()
    {
        _tipIndex = (_tipIndex + 1) % AppTips.Count;
        CurrentTip = AppTips.GetByIndex(_tipIndex);
    }

    /// -------------
    /// Salta manualmente al siguiente tip (útil para debug o para que el
    /// operador pueda pedir otro tip si el actual no le interesa).
    /// -------------
    [RelayCommand]
    private void SkipTip() => AdvanceTip();

    // ═════════════════════════════════════════════════════════════════════
    // Panel "En reproduccion" y "Siguiente"
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Actualiza las propiedades NowPlaying* con la info del canal activo
    /// según la prioridad del audio al aire y luego de las preescuchas.
    /// -------------
    private void RefreshNowPlayingPanel()
    {
        try
        {
            // 1) Scheduler (tanda o single file).
            //    IMPORTANTE: gateamos por _currentPlaybackInfo y NO por
            //    CurrentChannel. Las locuciones no tienen canal de audio
            //    (CurrentChannel == null) pero sí están "Playing" en el engine.
            //    El TandaTrackTitle ya viene con "Locución: Hora + minutos"
            //    cuando corresponde (asignado en PlaybackEngine.PlayLocutionInTanda).
            if (_currentPlaybackInfo.IsActive)
            {
                var info = _currentPlaybackInfo;
                var schedChannel = _playback.CurrentChannel;

                if (info.IsTanda)
                {
                    var title = !string.IsNullOrWhiteSpace(info.TandaTrackTitle)
                        ? info.TandaTrackTitle
                        : LocalizationService.GetString("Loc.Main.Source.Sequence");

                    string secondary;
                    if (schedChannel is not null)
                    {
                        // Es audio de tanda: podemos leer tags.
                        var tags = GetTagsCached(_playback.CurrentFilePath);
                        secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                            ? tags!.Performers
                            : (_playback.IsEncadenadorPlaying
                                ? LocalizationService.GetString("Loc.Main.Source.Chainer")
                                : string.Format(LocalizationService.GetString("Loc.Main.Source.SequenceNamed"), info.TandaNameText));
                    }
                    else
                    {
                        // Es locución de tanda: sin archivo, sin tags.
                        secondary = string.Format(LocalizationService.GetString("Loc.Main.Source.SequenceNamed"), info.TandaNameText);
                    }

                    NowPlayingTitle = title;
                    NowPlayingArtist = secondary;
                    NowPlayingPosition = _playback.IsEncadenadorPlaying
                        ? LocalizationService.GetString("Loc.Main.Source.Chainer")
                        : string.Format(LocalizationService.GetString("Loc.Main.TrackPosition"), info.TandaTrackIndex + 1, info.TandaTrackCount);
                    NowPlayingChannel = schedChannel;
                    IsNowPlayingActive = true;
                    return;
                }

                // SingleFile: solo puede ser audio (las locuciones van por tanda).
                if (schedChannel is not null && schedChannel.State == PlaybackState.Playing)
                {
                    var path = _playback.CurrentFilePath;
                    var tags = GetTagsCached(path);

                    NowPlayingTitle = ResolveDisplayTitle(tags, SafeFileName(path));
                    NowPlayingArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : (_playback.IsEncadenadorPlaying
                            ? LocalizationService.GetString("Loc.Main.Source.Chainer")
                            : !string.IsNullOrWhiteSpace(info.SourceListName)
                            ? string.Format(LocalizationService.GetString("Loc.Main.Source.ScheduledNamed"), info.SourceListName)
                            : LocalizationService.GetString("Loc.Main.Source.Scheduled"));
                    NowPlayingPosition = _playback.IsEncadenadorPlaying ? LocalizationService.GetString("Loc.Main.Source.Chainer") : string.Empty;
                    NowPlayingChannel = schedChannel;
                    IsNowPlayingActive = true;
                    return;
                }
            }

            // 2) Playlist (cualquier pestaña que este sonando).
            //    Las locuciones sueltas en playlist también tienen
            //    CurrentChannel == null pero IsPlayingNow == true.
            var activePl = GetActivePlaylistPlayer();
            if (activePl is not null)
            {
                var playlist = activePl.CurrentPlaylist;
                int idx = activePl.CurrentIndex;
                var plChannel = activePl.CurrentChannel;

                bool hasChannel = plChannel is not null && plChannel.State == PlaybackState.Playing;
                bool isLocution = plChannel is null && activePl.IsPlayingNow;

                if (hasChannel || isLocution)
                {
                    string title;
                    string secondary;

                    if (isLocution)
                    {
                        // Locución suelta en playlist: usar DisplayTitle del track.
                        title = (playlist is not null && idx >= 0 && idx < playlist.Tracks.Count)
                            ? playlist.Tracks[idx].DisplayTitle
                            : LocalizationService.GetString("Loc.Main.Source.VoiceOver");
                        secondary = playlist?.DisplayName ?? "Playlist";
                    }
                    else
                    {
                        var path = activePl.CurrentFilePath;
                        var tags = GetTagsCached(path);
                        title = ResolveDisplayTitle(tags, SafeFileName(path));
                        secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                            ? tags!.Performers
                            : (activePl.IsEncadenadorPlaying
                                ? LocalizationService.GetString("Loc.Main.Source.Chainer")
                                : playlist?.DisplayName ?? "Playlist");
                    }

                    NowPlayingTitle = title;
                    NowPlayingArtist = secondary;
                    NowPlayingPosition = activePl.IsEncadenadorPlaying
                        ? LocalizationService.GetString("Loc.Main.Source.Chainer")
                        : (playlist is not null && idx >= 0)
                            ? string.Format(LocalizationService.GetString("Loc.Main.TrackPosition"), idx + 1, playlist.Tracks.Count)
                            : string.Empty;
                    NowPlayingChannel = plChannel;
                    IsNowPlayingActive = true;
                    return;
                }
            }

            // 3) Otros reproductores.
            var (channel, source) = GetOtherNowPlayingAudio();
            if (channel is not null)
            {
                var path = channel.FilePath;
                var tags = GetTagsCached(path);

                NowPlayingTitle = ResolveDisplayTitle(tags, SafeFileName(path));
                NowPlayingArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : source;
                NowPlayingPosition = string.Empty;
                NowPlayingChannel = channel;
                IsNowPlayingActive = true;
                return;
            }

            if (_locution.IsPlaying)
            {
                NowPlayingTitle = LocalizationService.GetString("Loc.Main.Source.HourWeather");
                NowPlayingArtist = LocalizationService.GetString("Loc.Main.Source.Locution");
                NowPlayingPosition = string.Empty;
                NowPlayingChannel = null;
                IsNowPlayingActive = true;
                return;
            }

            // 4) Nada sonando.
            NowPlayingTitle = string.Empty;
            NowPlayingArtist = string.Empty;
            NowPlayingPosition = string.Empty;
            NowPlayingChannel = null;
            IsNowPlayingActive = false;
        }
        catch
        {
            IsNowPlayingActive = !string.IsNullOrWhiteSpace(NowPlayingTitle);
        }
    }

    private (IAudioChannel? Channel, string Source) GetOtherNowPlayingAudio()
    {
        if (GetAnyActiveCartChannel() is { } cart)
            return (cart, "Cart");
        if (_emergency.CurrentChannel is { State: PlaybackState.Playing } emergency)
            return (emergency, LocalizationService.GetString("Loc.Main.Emergency.Active"));
        if (_currentChannel is { State: PlaybackState.Playing } manual)
            return (manual, LocalizationService.GetString("Loc.Label.Module.Manual"));
        if (FileBrowser.PreviewChannel is { State: PlaybackState.Playing } preview)
            return (preview, LocalizationService.GetString("Loc.Label.Module.FileManager"));

        foreach (var tab in TabHostRight.Tabs)
        {
            if (tab.Content is FavoriteFolderPanelViewModel folder &&
                folder.PreviewChannel is { State: PlaybackState.Playing } favoritePreview)
                return (favoritePreview, LocalizationService.GetString("Loc.Label.Module.FavoriteFolder"));
        }

        if (Demo?.GetCurrentChannel() is { State: PlaybackState.Playing } demo)
            return (demo, LocalizationService.GetString("Loc.Main.Source.Demo"));

        return (null, string.Empty);
    }

    private void RefreshNextCountdown()
    {
        double? remaining = null;
        if (IsNextPlayingActive && IsNowPlayingActive)
        {
            try
            {
                var channel = NowPlayingChannel;
                if (channel is { State: PlaybackState.Playing })
                {
                    var end = channel.Duration - channel.TrimEnd;
                    if (end > TimeSpan.Zero)
                        remaining = Math.Max(0, (end - channel.Position).TotalSeconds);
                }
                else if (channel is null)
                {
                    remaining = _locution.RemainingPlaybackSeconds;
                }
            }
            catch
            {
                // The source may have finished or been released during this refresh.
            }
        }

        NextCountdownText = remaining is >= 0 and <= 9 ? $"{Math.Ceiling(remaining.Value):0}" : string.Empty;
    }

    /// -------------
    /// Actualiza las propiedades Next* con el proximo item a reproducir.
    /// Orden de prioridad:
    ///   1. Cola del scheduler (items pendientes).
    ///   2. Item suspendido del scheduler (que se va a reanudar).
    ///   3. Proxima pista de la playlist.
    ///   4. "(sin proximo)".
    /// -------------
    private void RefreshNextPlayingPanel()
    {
        try
        {
            // ═══════════════════════════════════════════════════════════════
            // 0) PRIORIDAD MÁXIMA: si estamos DENTRO de una tanda (audio o
            //    locución) y hay tracks pendientes, el "siguiente" es el
            //    próximo track de la misma tanda.
            //
            //    NOTA: gateamos por _currentPlaybackInfo y NO por
            //    CurrentChannel. Las locuciones no tienen canal de audio,
            //    pero sí están "Playing" en el engine.
            // ═══════════════════════════════════════════════════════════════
            bool schedulerIsPlaying =
                _currentPlaybackInfo.Kind == PlaybackStateKind.Playing ||
                _currentPlaybackInfo.Kind == PlaybackStateKind.Suspended;

            if (schedulerIsPlaying)
            {
                var nextEncadenador = _playback.GetUpcomingEncadenadorInfo();
                if (nextEncadenador is not null)
                {
                    var chain = nextEncadenador.Value;
                    var tags = GetTagsCached(chain.FilePath);
                    NextTitle = ResolveDisplayTitle(tags, chain.Title);
                    NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : LocalizationService.GetString("Loc.Main.Source.Chainer");
                    NextPosition = string.Empty;
                    IsNextPlayingActive = true;
                    return;
                }

                var nextTanda = _playback.GetNextTandaTrackInfo();
                if (nextTanda is not null)
                {
                    // Si el próximo track es locución, FilePath viene vacío.
                    // No intentamos leer tags en ese caso.
                    var nextPath = nextTanda.Value.FilePath;
                    RadioMaker.Core.Tags.AudioTagInfo? tags = null;
                    if (!string.IsNullOrWhiteSpace(nextPath))
                        tags = GetTagsCached(nextPath);

                    NextTitle = ResolveDisplayTitle(tags, nextTanda.Value.Title);

                    NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : LocalizationService.GetString("Loc.Main.Source.SequenceInProgress");

                    NextPosition = string.Format(LocalizationService.GetString("Loc.Main.TrackPosition"), nextTanda.Value.Index + 1, nextTanda.Value.Count);
                    IsNextPlayingActive = true;
                    return;
                }
            }

            // 1) Cola del scheduler (items pendientes fuera de la tanda actual).
            var queue = _playback.GetQueuedItems();
            if (queue.Count > 0)
            {
                var next = queue[0];

                NextTitle = next.Title;

                NextArtist = next.IsTanda && next.TandaTrackCount > 0
                    ? string.Format(LocalizationService.GetString("Loc.Main.Source.SequenceQueue"), next.TandaTrackCount)
                    : (!string.IsNullOrWhiteSpace(next.ListName)
                        ? string.Format(LocalizationService.GetString("Loc.Main.Source.FromList"), next.ListName)
                        : string.Empty);

                NextPosition = string.Empty;
                IsNextPlayingActive = true;
                return;
            }

            // 2) Suspendido del scheduler.
            var suspendedTitle = _playback.GetSuspendedItemTitle();
            if (!string.IsNullOrWhiteSpace(suspendedTitle))
            {
                NextTitle = suspendedTitle;

                var tandaInfo = _playback.GetSuspendedTandaInfo();
                if (tandaInfo is not null)
                {
                    NextArtist = tandaInfo.Value.TrackTitle;
                    NextPosition = string.Format(LocalizationService.GetString("Loc.Main.TrackPosition"), tandaInfo.Value.Index + 1, tandaInfo.Value.Count);
                }
                else
                {
                    NextArtist = LocalizationService.GetString("Loc.Main.Next.Resuming");
                    NextPosition = string.Empty;
                }
                IsNextPlayingActive = true;
                return;
            }

            // 3) Próxima pista de la playlist activa.
            var activePl = GetActivePlaylistPlayer();
            var nextPlaylistEncadenador = activePl?.GetUpcomingEncadenadorInfo();
            if (nextPlaylistEncadenador is not null)
            {
                var chain = nextPlaylistEncadenador.Value;
                var tags = GetTagsCached(chain.FilePath);
                NextTitle = ResolveDisplayTitle(tags, chain.Title);
                NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : LocalizationService.GetString("Loc.Main.Source.Chainer");
                NextPosition = string.Empty;
                IsNextPlayingActive = true;
                return;
            }

            var upcoming = activePl?.GetUpcomingTrackInfo();
            if (upcoming is null && NowPlayingChannel is not null &&
                ReferenceEquals(NowPlayingChannel, _emergency.CurrentChannel))
            {
                upcoming = _emergency.GetUpcomingTrackInfo();
            }
            if (upcoming is not null)
            {
                // Locuciones: FilePath vacío → no intentamos leer tags.
                var nextPath = upcoming.Value.FilePath;
                RadioMaker.Core.Tags.AudioTagInfo? tags = null;
                if (!string.IsNullOrWhiteSpace(nextPath))
                    tags = GetTagsCached(nextPath);

                NextTitle = ResolveDisplayTitle(tags, upcoming.Value.Title);

                NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : string.Empty;

                NextPosition = string.Format(LocalizationService.GetString("Loc.Main.TrackPosition"), upcoming.Value.Index + 1, upcoming.Value.Total);
                IsNextPlayingActive = true;
                return;
            }

            // 4) Nada en el horizonte.
            NextTitle = LocalizationService.GetString("Loc.Main.Next.None");
            NextArtist = string.Empty;
            NextPosition = string.Empty;
            IsNextPlayingActive = false;
        }
        catch
        {
            IsNextPlayingActive = !string.IsNullOrWhiteSpace(NextTitle)
                && NextTitle != LocalizationService.GetString("Loc.Main.Next.None");
        }
    }

    /// -------------
    /// Devuelve el nombre del archivo sin extension, con un fallback seguro
    /// si el path es vacio o invalido.
    /// -------------
    private static string SafeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// -------------
    /// Resuelve el titulo a mostrar para un audio, aplicando la cadena
    /// de fallbacks:
    ///   1. Title del TAG (si esta cargado y no vacio).
    ///   2. Fallback provisto (nombre de archivo, titulo de track, etc.).
    ///
    /// Nota: NO usamos "tags?.DisplayTitle ?? fallback" porque DisplayTitle
    /// devuelve string vacio cuando el Title esta vacio (no null), y el
    /// operador ?? solo captura null. Este helper cubre ambos casos.
    /// -------------
    private static string ResolveDisplayTitle(
        RadioMaker.Core.Tags.AudioTagInfo? tags,
        string? fallback)
    {
        if (tags is not null && !string.IsNullOrWhiteSpace(tags.DisplayTitle))
            return tags.DisplayTitle;

        return fallback ?? string.Empty;
    }

    /// -------------
    /// Lee los TAGs del path con un cache de 1 entrada. Si el path no cambio
    /// desde la ultima lectura, devuelve el cacheado sin tocar el disco.
    /// -------------
    private RadioMaker.Core.Tags.AudioTagInfo? GetTagsCached(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (string.Equals(path, _tagCachePath, StringComparison.OrdinalIgnoreCase))
            return _tagCacheInfo;

        try
        {
            var info = RadioMaker.Core.Tags.AudioTagReader.Read(path);
            _tagCachePath = path;
            _tagCacheInfo = info;
            return info;
        }
        catch
        {
            _tagCachePath = path;
            _tagCacheInfo = null;
            return null;
        }
    }

    /// -------------
    /// Devuelve el canal que debe usarse para el LED de tiempo, aplicando
    /// la prioridad: scheduler > playlist > cart > manual. Devuelve null
    /// si ninguno esta reproduciendo.
    /// -------------
    private IAudioChannel? GetPriorityChannel()
    {
        try
        {
            var sched = _playback.CurrentChannel;
            if (sched is not null && sched.State == PlaybackState.Playing)
                return sched;
        }
        catch { }

        var pl = GetAnyPlaylistChannel();
        if (pl is not null && pl.State == PlaybackState.Playing)
            return pl;

        try
        {
            var autoDj = _autoDjPlayer.CurrentChannel;
            if (autoDj is not null && autoDj.State == PlaybackState.Playing) return autoDj;
        }
        catch { }

        var cart = GetAnyActiveCartChannel();
        if (cart is not null) return cart;

        try
        {
            if (_currentChannel is not null && _currentChannel.State == PlaybackState.Playing)
                return _currentChannel;
        }
        catch { }

        return null;
    }

    /// -------------
    /// Formatea un TimeSpan como HH:MM:SS para el LED (2 digitos por campo,
    /// horas sin limite superior).
    /// -------------
    private static string FormatLedTime(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;

        int hours = (int)ts.TotalHours;
        if (hours > 99) hours = 99;

        return $"{hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private void RefreshPosition()
    {
        // 1) Actualizar el canal del Spectrum si cambio.
        UpdateSpectrumChannel();

        // 2) Actualizar posicion / duracion.
        var ch = ActiveChannel;
        if (ch is null) return;

        try
        {
            var pos = ch.Position;
            var dur = ch.Duration;

            if (_currentChannel is null)
            {
                Position = pos;
                Duration = dur;

                if (PlaybackState != PlaybackState.Playing)
                {
                    PlaybackState = PlaybackState.Playing;
                    RefreshCommandStates();
                }
            }
            else
            {
                Position = pos;
            }
        }
        catch
        {
            _positionTimer.Stop();
        }

        OnPropertyChanged(nameof(PositionText));
    }

    /// -------------
    /// Determina cual es el canal activo ahora y lo propaga al panel Spectrum
    /// si cambio respecto al tick anterior. Tambien detecta la etiqueta de
    /// origen (MANUAL / CART / SCHEDULER / PLAYLIST).
    /// -------------
    private void UpdateSpectrumChannel()
    {
        if (_locution.IsPlaying && _locution.CurrentSpectrum is { } locutionSpectrum)
        {
            Spectrum.SetSpectrumSource(
                locutionSpectrum,
                LocalizationService.GetString("Loc.Main.Source.Locution"),
                LocalizationService.GetString("Loc.Main.Source.HourWeather"));
            return;
        }

        IAudioChannel? active = null;
        string source = string.Empty;

        if (_currentChannel is not null)
        {
            active = _currentChannel;
            source = LocalizationService.GetString("Loc.Main.Source.Manual");
        }
        else
        {
            var sched = _playback.CurrentChannel;
            if (sched is not null)
            {
                active = sched;
                source = LocalizationService.GetString("Loc.Main.Source.Scheduler");
            }
            else
            {
                // ── FASE B: buscar la primera playlist que este sonando.
                IAudioChannel? pl = null;
                foreach (var p in _playlistPlayers)
                {
                    try
                    {
                        var ch = p.CurrentChannel;
                        if (ch is not null) { pl = ch; break; }
                    }
                    catch { }
                }

                if (pl is not null)
                {
                    active = pl;
                    source = LocalizationService.GetString("Loc.Main.Source.Playlist");
                }
                else
                {
                    var autoDj = _autoDjPlayer.CurrentChannel;
                    if (autoDj is not null)
                    {
                        active = autoDj;
                        source = LocalizationService.GetString("Loc.Main.Source.AutoDj");
                    }
                    else
                    {
                        var cart = GetAnyActiveCartChannel();
                        if (cart is not null)
                        {
                            active = cart;
                            source = LocalizationService.GetString("Loc.Main.Source.Cart");
                        }
                        else
                        {
                            // Demo de bienvenida: prioridad más baja que todo.
                            var demoCh = Demo?.GetCurrentChannel();
                            if (demoCh is not null)
                            {
                                active = demoCh;
                                source = LocalizationService.GetString("Loc.Main.Source.Demo");
                            }
                        }
                    }
                }
            }
        }

        Spectrum.SetChannel(active, source);
    }

    private void RefreshMeter()
    {
        var locutionMeter = _locution.IsPlaying ? _locution.CurrentMeter : null;
        var ch = ActiveChannel;
        var meter = locutionMeter ?? ch?.Meter;
        if (meter is null) return;

        AudioLevels levels;
        try { levels = meter.Read(); }
        catch { _meterTimer.Stop(); return; }

        VuLeftLevel = levels.LeftPeak;
        VuRightLevel = levels.RightPeak;
        VuLeftRms = levels.LeftRms;
        VuRightRms = levels.RightRms;

        double decayPerTick = 33.0 / Math.Max(200, VuSettings.PeakHoldDecayMs);
        VuLeftPeak = Math.Max(VuLeftLevel, VuLeftPeak - decayPerTick);
        VuRightPeak = Math.Max(VuRightLevel, VuRightPeak - decayPerTick);
        if (VuLeftPeak < 0) VuLeftPeak = 0;
        if (VuRightPeak < 0) VuRightPeak = 0;

        VuClip = levels.AnyClip || (VuClip && VuLeftLevel > 0.01);
        if (!levels.AnyClip && VuLeftLevel < 0.5 && VuRightLevel < 0.5) VuClip = false;
    }

    private void ResetMeter()
    {
        VuLeftLevel = 0; VuRightLevel = 0;
        VuLeftPeak = 0; VuRightPeak = 0;
        VuLeftRms = 0; VuRightRms = 0;
        VuClip = false;
    }

    /// -------------
    /// Sincroniza el field PlaybackState con el estado REAL del canal activo.
    /// Se llama desde los handlers de cambio de estado y desde los timers,
    /// para que el status bar refleje lo que realmente esta pasando en
    /// CUALQUIER modulo (cart, playlist, scheduler, manual).
    ///
    /// Politica:
    ///   • Si habia Playing/Paused y ya no hay canal activo → Stopped.
    ///   • Si el canal activo cambio de estado → reflejar ese estado.
    ///   • Si estaba en Ended/Error/Stopped → no tocar (respeta el mensaje
    ///     transitorio de "Fin de reproduccion").
    /// -------------
    private void SyncPlaybackState()
    {
        try
        {
            bool anyPlaying = false;
            bool anyPaused = false;
            bool anyStalled = false;

            // Revisar el estado real de todos los canales conocidos. Un
            // canal manual cargado pero detenido no debe ocultar otra fuente
            // que esté reproduciendo.
            foreach (var channel in GetAllActiveChannels())
            {
                if (channel is null) continue;
                try
                {
                    switch (channel.State)
                    {
                        case PlaybackState.Playing:
                            anyPlaying = true;
                            break;
                        case PlaybackState.Paused:
                            anyPaused = true;
                            break;
                        case PlaybackState.Stalled:
                            anyStalled = true;
                            break;
                    }
                }
                catch { }
            }

            // Algunas locuciones no exponen IAudioChannel. Consultar el
            // estado lógico del Scheduler/Playlist y el servicio de voz.
            if (_currentPlaybackInfo.Kind == PlaybackStateKind.Playing ||
                _locution.IsPlaying)
                anyPlaying = true;

            var playlistPlayers = new List<PlaylistPlayer> { _playlistPlayer, _autoDjPlayer };
            playlistPlayers.AddRange(_playlistPlayers);
            foreach (var player in playlistPlayers)
            {
                try
                {
                    if (player.IsPlayingNow ||
                        player.CurrentStatus is PlaylistPlayerStatus.Playing or PlaylistPlayerStatus.Delaying)
                    {
                        anyPlaying = true;
                    }
                    else if (player.CurrentStatus is PlaylistPlayerStatus.Paused or PlaylistPlayerStatus.PausedByScheduler)
                        anyPaused = true;
                }
                catch { }
            }

            if (_currentPlaybackInfo.Kind == PlaybackStateKind.Suspended)
                anyPaused = true;

            try { anyPaused |= _cartPlayer.HasSuspendedChannels; } catch { }
            foreach (var cart in _extraCartPlayers)
            {
                try { anyPaused |= cart.HasSuspendedChannels; } catch { }
            }

            // Si hay varias fuentes simultáneas, el estado audible tiene
            // prioridad sobre otra fuente pausada/suspendida.
            var resolved = anyPlaying
                ? PlaybackState.Playing
                : anyPaused
                    ? PlaybackState.Paused
                    : anyStalled
                        ? PlaybackState.Stalled
                        : PlaybackState.Stopped;

            // Mantener el aviso de fin hasta que comience otra reproducción
            // o el usuario detenga/cargue otro canal.
            if (resolved == PlaybackState.Stopped && PlaybackState == PlaybackState.Ended)
                return;

            if (PlaybackState == resolved) return;
            PlaybackState = resolved;
            RefreshCommandStates();
        }
        catch { }
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(PlaybackStateText));
        RefreshNowPlayingPanel();
        RefreshNextPlayingPanel();
    }

    private PlaybackStateKind _lastPlaybackEngineKind = PlaybackStateKind.Idle;

    /// -------------
    /// True si hay algo reproduciendo en el bus del Scheduler (el "aire").
    /// Se actualiza en UpdateTabLiveStates y alimenta el indicador visual
    /// "AL AIRE" del menu principal.
    /// -------------
    private bool _isAlAire;

    // Estado actual de los reproductores (para el panel "En reproduccion" / "Siguiente").
    private PlaybackStateInfo _currentPlaybackInfo = PlaybackStateInfo.Idle;
    private PlaylistPlayerState _currentPlaylistInfo = PlaylistPlayerState.Stopped;

    // Cache de TAGs del ultimo archivo leido (evita releer cada 500ms).
    private string? _tagCachePath;
    private RadioMaker.Core.Tags.AudioTagInfo? _tagCacheInfo;

    partial void OnVolumeChanged(double value)
    {
        // Aplicar al volumen global del engine (afecta a TODOS los canales
        // de TODOS los reproductores: cart, playlist, scheduler, manual).
        try { _engine.GlobalVolume = value; }
        catch (Exception ex) { _logService.Error("SETTINGS VOLUME", "No se pudo aplicar el volumen global", ex); }

        // Persistir en disco para el proximo arranque.
        try { VolumeSettingsStore.Save(value); }
        catch (Exception ex) { _logService.Error("SETTINGS VOLUME", "No se pudo guardar el volumen global", ex); }

        _logService.Info("SETTINGS VOLUME", $"Volumen global ajustado a {value:P0}.");

        OnPropertyChanged(nameof(VolumeText));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Routing de buses por modulo
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Recalcula el bus efectivo de cada modulo y lo aplica al reproductor
    /// correspondiente. Se llama al arrancar, al cambiar el modo global, al
    /// cambiar el bus global, y al agregar/quitar buses.
    /// -------------
    private void ApplyRoutingToPlayers()
    {
        try { _playback.SetBus(_routing.GetEffectiveBus(AudioModuleKind.Scheduler)); } catch { }

        // ── FASE B / C: las playlists y los carts tienen bus POR PESTAÑA.
        // No aplicamos el routing del modulo a las pestañas existentes.
        // El modulo solo se usa como default al crear pestañas nuevas
        // (via GetCartDefaultBus / GetSchedulerBus).
    }

    /// -------------
    /// Handler de cambios generales del routing (toggle global, cambio de
    /// bus global). Reaplica todo.
    /// -------------
    private void OnRoutingChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyRoutingToPlayers();
            OnPropertyChanged(nameof(UseGlobalBus));

            // Si cambio el AirBus, refrescar el indicador inmediatamente.
            try { UpdateTabLiveStates(); } catch { }

            if (_routing.UseGlobalBus)
                SetLocalizedStatus("Loc.Main.Status.GlobalBusApplied", _routing.GlobalBus.DisplayName);
            else
                SetLocalizedStatus("Loc.Main.Status.PerModuleRouting");
            _logService.Info("SETTINGS ROUTING",
                _routing.UseGlobalBus
                    ? $"Modo global activo; bus={_routing.GlobalBus.DisplayName}."
                    : "Modo de ruteo individual por módulo activo.");
        });
    }

    /// -------------
    /// Handler de cambio especifico del bus de un modulo. Solo reaplica
    /// ese modulo, no los otros.
    /// -------------
    private void OnModuleBusChanged(object? sender, AudioModuleKind kind)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var bus = _routing.GetEffectiveBus(kind);
            _logService.Info("SETTINGS ROUTING", $"Bus predeterminado para {kind}: {bus.DisplayName}.");

            switch (kind)
            {
                case AudioModuleKind.Cart:
                    // ── FASE C: el bus del cart es POR PESTAÑA. Este handler
                    // ya no toca pestañas existentes. El modulo Cart solo
                    // se usa como default al crear pestañas nuevas.
                    SetLocalizedStatus("Loc.Main.Status.DefaultCartBus", bus.DisplayName);
                    break;

                case AudioModuleKind.Playlist:
                    // ── FASE B: el bus de playlist es POR PESTAÑA.
                    // Este handler ya no toca pestañas existentes. El
                    // modulo Playlist solo se usa como default al crear
                    // pestañas nuevas.
                    SetLocalizedStatus("Loc.Main.Status.DefaultPlaylistBus", bus.DisplayName);
                    break;

                case AudioModuleKind.Scheduler:
                    try { _playback.SetBus(bus); } catch { }
                    SetLocalizedStatus("Loc.Main.Status.DefaultScheduledBus", bus.DisplayName);
                    break;

                case AudioModuleKind.FileManager:
                    // Reservado para preescucha del File Manager.
                    SetLocalizedStatus("Loc.Main.Status.DefaultFileManagerBus", bus.DisplayName);
                    break;
            }
        });
    }

    partial void OnSelectedBusChanged(IAudioBus? oldValue, IAudioBus? newValue)
    {
        OnPropertyChanged(nameof(SelectedBusDisplayText));
        if (_suppressBusSelectionHook) return;
        if (ReferenceEquals(oldValue, newValue)) return;
        if (newValue is null) return;

        // El combo global del header ahora actua como "bus global".
        // El routing se encarga de propagar a los modulos segun el modo.
        _routing?.SetGlobalBus(newValue);
        _logService.Info("SETTINGS ROUTING", $"Bus global seleccionado: {newValue.DisplayName}.");

        if (HasFile)
        {
            ReleaseCurrentChannel();
            SetLocalizedStatus("Loc.Main.Status.ChannelReleasedForBusChange");
        }

        RefreshCommandStates();
    }

    // ───── Workspace (persistencia de pestañas) ──────────────────────────

    /// -------------
    /// Guarda el estado del workspace: recolecta las entradas de las
    /// zonas izquierda y central, y las persiste junto con el indice
    /// activo de cada zona.
    /// -------------
    public void SaveWorkspace()
    {
        try
        {
            // ── 1) Guardar TODAS las botoneras ──────────────────────────
            //    Las botoneras NO se persisten con el workspace: tienen su
            //    propio archivo .rcb. Hay que guardar cada una en su ruta.
            SaveAllCartBoards();

            // ── 2) Guardar el workspace de pestañas ─────────────────────
            var entries = new List<WorkspaceTabEntry>();

            // Zona izquierda.
            var leftEntries = TabHost.BuildWorkspaceEntries();
            entries.AddRange(leftEntries);

            // Zona central.
            var centerEntries = TabHostCenter.BuildWorkspaceEntries();
            entries.AddRange(centerEntries);

            // Zona derecha.
            var rightEntries = TabHostRight.BuildWorkspaceEntries();
            entries.AddRange(rightEntries);

            // Indices activos por zona.
            int leftActiveIndex = GetClosableIndex(TabHost);
            int centerActiveIndex = GetClosableIndex(TabHostCenter);
            int rightActiveIndex = GetClosableIndex(TabHostRight);

            var state = new WorkspaceState
            {
                Tabs = entries,
                ActiveTabIndex = leftActiveIndex < 0 ? 0 : leftActiveIndex,
                ActiveTabIndexLeft = leftActiveIndex < 0 ? 0 : leftActiveIndex,
                ActiveTabIndexCenter = centerActiveIndex < 0 ? 0 : centerActiveIndex,
                ActiveTabIndexRight = rightActiveIndex < 0 ? 0 : rightActiveIndex,

                // Persistir la ruta del cart principal. Al reabrir la app,
                // si existe, se carga este archivo en lugar del default.
                PrimaryCartPath = CartBoard?.LoadedFilePath ?? string.Empty,
            };

            WorkspaceStateStore.Save(state);
        }
        catch (Exception ex)
        {
            _logService.Error("WORKSPACE", "No se pudo guardar el workspace", ex);
        }
    }

    /// -------------
    /// Guarda TODAS las botoneras (la principal y las extras) en su
    /// archivo .rcb correspondiente. Se llama desde SaveWorkspace al
    /// cerrar la app para no perder cambios sin guardar.
    ///
    /// Las botoneras que nunca fueron guardadas en un .rcb (extras
    /// nuevas) se guardan en la ruta por defecto SOLO si tienen algun
    /// boton con archivo asignado. Asi no generamos archivos .rcb basura.
    /// -------------
    private void SaveAllCartBoards()
    {
        // Botonera principal: siempre se guarda, aunque este vacia. Su
        // ruta por defecto es %APPDATA%/RadioMaker/cartboard.rcb.
        try
        {
            if (CartBoard is not null)
            {
                var path = CartBoard.LoadedFilePath ?? CartBoardStore.DefaultPath;
                CartBoard.SaveToFile(path);
            }
        }
        catch (Exception ex)
        {
            _logService.Error("CART", "No se pudo guardar la botonera principal", ex);
        }

        // Botoneras extras (pestañas abiertas en la zona central).
        // Solo guardamos las que ya tienen una ruta asignada (fueron
        // guardadas alguna vez con "Guardar como...").
        try
        {
            foreach (var tab in TabHostCenter.Tabs)
            {
                if (tab.Content is CartBoardViewModel extraCart)
                {
                    if (string.IsNullOrEmpty(extraCart.LoadedFilePath))
                        continue;

                    try { extraCart.SaveToFile(extraCart.LoadedFilePath); }
                    catch (Exception ex) { _logService.Error("CART", "No se pudo guardar una botonera adicional", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error("CART", "Error al guardar las botoneras", ex);
        }
    }

    /// -------------
    /// Restaura el workspace: reparte las entradas entre las zonas segun
    /// su campo Zone, y reaplica el indice activo de cada zona.
    /// -------------
    public void RestoreWorkspace()
    {
        var state = WorkspaceStateStore.Load();
        if (state is null)
        {
            // Nada que restaurar.
            return;
        }

        // ── Restaurar el cart principal (pestaña fija del centro) ────────
        //    Si el usuario tenia abierto un .rcb especifico, cargarlo.
        if (!string.IsNullOrEmpty(state.PrimaryCartPath) &&
            File.Exists(state.PrimaryCartPath))
        {
            try { CartBoard?.LoadFromFile(state.PrimaryCartPath); } catch { }
        }

        if (state.Tabs.Count == 0)
        {
            // Sin tabs que restaurar. El cart principal ya esta cargado.
            return;
        }

        // Repartir las entradas por zona. Las Scheduled que aparezcan primero
        // en cada zona se cargan en la ScheduledList pre-existente del
        // TabHost de esa zona (evita duplicar la pestaña Programados).

        foreach (var entry in state.Tabs)
        {
            try
            {
                switch (entry.Zone)
                {
                    case WorkspaceTabZone.Left:
                        TabHost.TryRestoreEntry(entry, out _);
                        break;

                    case WorkspaceTabZone.Center:
                        TabHostCenter.TryRestoreEntry(entry, out _);
                        break;

                    case WorkspaceTabZone.Right:
                        TabHostRight.TryRestoreEntry(entry, out _);
                        break;
                }
            }
            catch { }
        }

        // Restaurar los indices activos.
        // NOTA: no restauramos el indice activo a proposito.
        //
        // El usuario pidio que al arrancar la app siempre quede marcada la
        // pestaña fija de cada zona:
        //   - Left   -> Eventos
        //   - Center -> Cart
        //   - Right  -> Explorador
        //
        // El constructor del TabHostViewModel ya se encarga de esto.
        // El ActiveTabIndex guardado en el JSON se conserva por si en el
        // futuro se decide usarlo, pero por ahora se ignora.
    }

    private static int GetClosableIndex(TabHostViewModel host)
    {
        int index = 0;
        foreach (var tab in host.Tabs)
        {
            if (!tab.IsClosable) continue;

            if (ReferenceEquals(tab, host.SelectedTab))
                return index;

            index++;
        }
        return 0;
    }

    /// -------------
    /// Resuelve un brush del tema actual a partir de una clave de recurso.
    /// Devuelve un brush transparente si la clave no existe o si no hay
    /// Application activa.
    /// -------------
    private static IBrush ResolveThemeBrush(string key)
    {
        try
        {
            var app = Avalonia.Application.Current;
            if (app is null)
                return new SolidColorBrush(Colors.Transparent);

            var variant = app.ActualThemeVariant;

            if (variant is not null &&
                app.TryGetResource(key, variant, out var resource) &&
                resource is not null)
            {
                if (resource is IBrush brush) return brush;
                if (resource is Color color) return new SolidColorBrush(color);
            }

            if (app.TryGetResource(key, null, out resource) && resource is not null)
            {
                if (resource is IBrush brush2) return brush2;
                if (resource is Color color2) return new SolidColorBrush(color2);
            }
        }
        catch { }

        return new SolidColorBrush(Colors.Transparent);
    }

    /// -------------
    /// Abre la ventana de configuracion en la seccion "Datos climaticos".
    /// Se dispara desde el menu contextual del panel de clima.
    /// -------------
    private void OpenSettingsFromPanel()
    {
        try
        {
            // Este metodo se sobreescribe en el code-behind si hace falta
            // abrir la ventana. Aca se emite un evento para que el
            // code-behind lo maneje.
            OpenSettingsRequested?.Invoke(this, "climate");
        }
        catch { }
    }

    /// -------------
    /// Lanza RMVoice.exe si esta presente en Addons/RMVoice/.
    /// -------------
    private void LaunchRMVoiceUI()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;

            string exeName = OperatingSystem.IsWindows()
                ? "RMVoice.exe"
                : "RMVoice";

            string[] candidates =
            {
                System.IO.Path.Combine(baseDir, "Addons", "RMVoice", exeName),
                System.IO.Path.Combine(baseDir, "Complementos", exeName),
                System.IO.Path.Combine(baseDir, "complementos", exeName),
                System.IO.Path.Combine(baseDir, exeName)
            };

            foreach (var path in candidates)
            {
                if (!System.IO.File.Exists(path)) continue;

                // ===== CROSS-PLATFORM =====
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? baseDir
                };
                psi.ArgumentList.Add("--from-radiomaker");

                System.Diagnostics.Process.Start(psi);

                SetLocalizedStatus("Loc.Main.Status.OpeningRMVoice");
                return;
            }

            try
            {
                _logService.Warning("ADDON-LAUNCHER",
                    "No se encontró RMVoice.exe. Verificar que esté en Addons/RMVoice/.");
            }
            catch { }

            SetLocalizedStatus("Loc.Main.Status.RMVoiceNotFound");
        }
        catch (Exception ex)
        {
            // ===== LOGGING =====
            try
            {
                _logService.Error("ADDON-LAUNCHER",
                    $"Error al abrir RMVoice desde el panel de clima: {ex.Message}", ex);
            }
            catch { }

            SetLocalizedStatus("Loc.Main.Status.OpenRMVoiceError", ex.Message);
        }
    }

    /// -------------
    /// Evento que el code-behind usa para abrir la ventana de configuracion
    /// en una seccion concreta.
    /// -------------
    public event EventHandler<string>? OpenSettingsRequested;
    public event EventHandler? OpenAutoDjPlannerRequested;

    /// -------------
    /// Devuelve el PlaylistPlayer que este REALMENTE reproduciendo. Si
    /// ninguno esta reproduciendo, devuelve el primero con una playlist
    /// cargada (pausado o preparado). (FASE B)
    /// -------------
    private PlaylistPlayer? GetActivePlaylistPlayer()
    {
        // AutoDJ usa un reproductor dedicado, fuera de la lista de playlists normales.
        try
        {
            if (_autoDjPlayer.IsPlayingNow) return _autoDjPlayer;
        }
        catch { }

        // Primera pasada: buscar uno que este PLAYING NOW.
        foreach (var p in _playlistPlayers)
        {
            try
            {
                if (p.IsPlayingNow) return p;
            }
            catch { }
        }

        // Segunda pasada: aceptar uno activo (pausado o preparado).
        try
        {
            if (_autoDjPlayer.IsActive && _autoDjPlayer.CurrentChannel is not null)
                return _autoDjPlayer;
        }
        catch { }

        foreach (var p in _playlistPlayers)
        {
            try
            {
                if (p.IsActive && p.CurrentChannel is not null)
                    return p;
            }
            catch { }
        }

        return null;
    }

    /// -------------
    /// Recalcula el estado "al aire" de todas las pestañas de los 3
    /// TabHosts, y actualiza el indicador visual (circulo rojo/amarillo).
    ///
    /// Reglas:
    ///   - Programados: Live si el engine reproduce un item de esa lista.
    ///                  PausedByScheduler si la lista tiene el item suspendido.
    ///   - Playlist:    Live si el player de esa pestaña esta sonando en el
    ///                  bus del Scheduler. PausedByScheduler si fue pausada
    ///                  por el scheduler.
    ///   - Tanda:       Live si el TandaName coincide con el del engine.
    /// -------------
    private void UpdateTabLiveStates()
    {
        try
        {
            var currentItemId = _playback.CurrentItemId;
            var suspendedItemId = _playback.SuspendedItemId;
            var airBusId = _routing.AirBusId;
            var currentTandaName = _currentPlaybackInfo.TandaNameText;

            ApplyStatesToHost(TabHost, currentItemId, suspendedItemId,
                              airBusId, currentTandaName);
            ApplyStatesToHost(TabHostCenter, currentItemId, suspendedItemId,
                              airBusId, currentTandaName);
            ApplyStatesToHost(TabHostRight, currentItemId, suspendedItemId,
                              airBusId, currentTandaName);

            UpdateAlAireIndicator();
        }
        catch { }
    }

    /// -------------
    /// Determina si hay algo reproduciendo en el bus del Scheduler:
    ///   - El PlaybackEngine reproduciendo cualquier item.
    ///   - O una playlist reproduciendo en el bus del Scheduler.
    /// -------------
    private void UpdateAlAireIndicator()
    {
        bool alAire = false;
        var airBusId = _routing.AirBusId;

        if (!string.IsNullOrEmpty(airBusId))
        {
            try
            {
                foreach (var channel in GetAllActiveChannels())
                {
                    try
                    {
                        if (channel.State == PlaybackState.Playing &&
                            string.Equals(channel.BusId, airBusId, StringComparison.OrdinalIgnoreCase))
                        {
                            alAire = true;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        if (_isAlAire == alAire) return;

        _isAlAire = alAire;

        OnPropertyChanged(nameof(IsAlAire));
        OnPropertyChanged(nameof(AlAireBackground));
        OnPropertyChanged(nameof(AlAireBorderBrush));
        OnPropertyChanged(nameof(AlAireLedColor));
        OnPropertyChanged(nameof(AlAireTextColor));
        OnPropertyChanged(nameof(AlAireText));
        OnPropertyChanged(nameof(SelectedBusDisplayText));
        OnPropertyChanged(nameof(AppSlogan));
    }

    private void ApplyStatesToHost(
        TabHostViewModel host,
        string? currentItemId,
        string? suspendedItemId,
        string airBusId,
        string currentTandaName)
    {
        foreach (var tab in host.Tabs)
        {
            try
            {
                tab.LiveState = ComputeTabLiveState(
                    tab, currentItemId, suspendedItemId,
                    airBusId, currentTandaName);
            }
            catch { }
        }
    }

    private TabLiveState ComputeTabLiveState(
        TabItemViewModel tab,
        string? currentItemId,
        string? suspendedItemId,
        string airBusId,
        string currentTandaName)
    {
        // ── Programados ─────────────────────────────────────────────────
        if (tab.Content is ScheduledListViewModel schedVm)
        {
            if (!string.IsNullOrEmpty(currentItemId) &&
                schedVm.ContainsItem(currentItemId))
                return TabLiveState.Live;

            if (!string.IsNullOrEmpty(suspendedItemId) &&
                schedVm.ContainsItem(suspendedItemId))
                return TabLiveState.PausedByScheduler;

            return TabLiveState.None;
        }

        // ── Playlist ────────────────────────────────────────────────────
        if (tab.Content is PlaylistEditorViewModel plVm)
        {
            // Propiedad publica: reemplaza reflection sobre _player.
            var player = plVm.Player;
            if (player is null) return TabLiveState.None;

            // Pausada por scheduler.
            if (player.IsPausedByScheduler)
                return TabLiveState.PausedByScheduler;

            // Sonando en el bus del scheduler.
            if (player.IsPlayingNow)
            {
                // Propiedad publica: reemplaza reflection sobre _targetBus.
                var playerBus = player.TargetBus;
                if (playerBus is not null &&
                    !string.IsNullOrEmpty(airBusId) &&
                    string.Equals(playerBus.Id, airBusId, StringComparison.OrdinalIgnoreCase))
                {
                    return TabLiveState.Live;
                }
            }

            return TabLiveState.None;
        }

        // ── Tanda ───────────────────────────────────────────────────────
        if (tab.Content is TandaEditorViewModel tandaVm)
        {
            if (!string.IsNullOrEmpty(currentTandaName) &&
                !string.IsNullOrWhiteSpace(tandaVm.TandaName) &&
                string.Equals(tandaVm.TandaName, currentTandaName, StringComparison.OrdinalIgnoreCase))
            {
                return TabLiveState.Live;
            }
            return TabLiveState.None;
        }

        // ── Cart ────────────────────────────────────────────────────────
        // El cart esta al aire si tiene algun canal reproduciendose, o
        // "pausado por coordinator" si tiene canales suspendidos (AutoResume).
        // El bus del cart debe coincidir con el bus de AIRE.
        if (tab.Content is CartBoardViewModel cartVm)
        {
            if (!string.IsNullOrEmpty(airBusId))
            {
                var cartBusId = cartVm.BusSelector?.SelectedBus?.Id;
                if (!string.IsNullOrEmpty(cartBusId) &&
                    string.Equals(cartBusId, airBusId, StringComparison.OrdinalIgnoreCase))
                {
                    if (cartVm.IsAnyPlaying)
                        return TabLiveState.Live;

                    if (cartVm.IsPausedByCoordinator)
                        return TabLiveState.PausedByScheduler;
                }
            }
            return TabLiveState.None;
        }

        // ── FavoriteFolder (preescucha) ─────────────────────────────────
        // Hoy solo muestra "al aire" cuando reproduce. El naranja de
        // "pausado" se activara cuando FavoriteFolder participe del
        // coordinator (Phase 2C). Dejo el branch preparado.
        if (tab.Content is FavoriteFolderPanelViewModel favVm)
        {
            if (!string.IsNullOrEmpty(airBusId))
            {
                var favBusId = favVm.BusSelector?.SelectedBus?.Id;
                if (!string.IsNullOrEmpty(favBusId) &&
                    string.Equals(favBusId, airBusId, StringComparison.OrdinalIgnoreCase))
                {
                    if (favVm.IsPlaying)
                        return TabLiveState.Live;

                    if (favVm.IsPausedByCoordinator)
                        return TabLiveState.PausedByScheduler;
                }
            }
            return TabLiveState.None;
        }

        // Los demas tipos (Notes, Custom, Events) no muestran indicador.
        return TabLiveState.None;
    }

    /// -------------
    /// Devuelve el primer canal de playlist activo (el que esta sonando
    /// ahora). Prioriza los que estan reproduciendo por sobre los pausados.
    /// (FASE B)
    /// -------------
    private IAudioChannel? GetAnyPlaylistChannel()
    {
        // Primera pasada: buscar uno que este PLAYING.
        foreach (var p in _playlistPlayers)
        {
            try
            {
                var ch = p.CurrentChannel;
                if (ch is not null && ch.State == PlaybackState.Playing)
                    return ch;
            }
            catch { }
        }

        // Segunda pasada: aceptar pausados (para que el panel muestre
        // info aunque este pausado, igual que antes).
        foreach (var p in _playlistPlayers)
        {
            try
            {
                var ch = p.CurrentChannel;
                if (ch is not null)
                    return ch;
            }
            catch { }
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Ducking (botón de micrófono en la toolbar)
    // ═════════════════════════════════════════════════════════════════════

    public string AutoDjButtonTooltip => IsAutoDjEnabled
        ? LocalizationService.GetString("Loc.Toolbar.AutoDJ.Disable")
        : _autoDjPreparedPlaylist is { Tracks.Count: > 0 } || File.Exists(AutoDjTemporaryPlaylistPath)
            ? LocalizationService.GetString("Loc.Toolbar.AutoDJ.ActivatePrepared")
            : LocalizationService.GetString("Loc.Toolbar.AutoDJ.Generate");

    public IBrush AutoDjButtonBackground => IsAutoDjEnabled
        ? new SolidColorBrush(Color.Parse("#16A34A"))
        : ResolveThemeBrush("BgPrimary") ?? new SolidColorBrush(Colors.Transparent);

    public IBrush AutoDjButtonForeground => IsAutoDjEnabled
        ? new SolidColorBrush(Colors.White)
        : ResolveThemeBrush("FgPrimary") ?? new SolidColorBrush(Colors.Gray);

    private void NotifyAutoDjButtonChanged()
    {
        OnPropertyChanged(nameof(AutoDjButtonTooltip));
        OnPropertyChanged(nameof(AutoDjButtonBackground));
        OnPropertyChanged(nameof(AutoDjButtonForeground));
    }

    /// -------------
    /// True si el ducking por micrófono está activo. Bindea al botón de la
    /// toolbar como estado y a las propiedades visuales (color/ícono/tooltip).
    /// -------------
    public bool IsDuckingActive => Ducking.IsEnabled;

    /// -------------Tooltip dinámico del botón.</summary>
    public string DuckingTooltip => IsDuckingActive
        ? LocalizationService.GetString("Loc.Toolbar.Ducking.Disable")
        : LocalizationService.GetString("Loc.Toolbar.Ducking.Enable");

    /// -------------Fondo del botón: rojo si activo, color base si inactivo.</summary>
    public IBrush DuckingButtonBackground => IsDuckingActive
        ? new SolidColorBrush(Color.Parse("#DC2626"))
        : ResolveThemeBrush("BgPrimary") ?? new SolidColorBrush(Colors.Transparent);

    /// -------------Color del ícono: blanco si activo, color primario si inactivo.</summary>
    public IBrush DuckingButtonForeground => IsDuckingActive
        ? new SolidColorBrush(Colors.White)
        : ResolveThemeBrush("FgPrimary") ?? new SolidColorBrush(Colors.Gray);

    [RelayCommand]
    private void ToggleDucking()
    {
        // El setter de IsEnabled en DuckingPanelViewModel ya se encarga de:
        //   - Persistir en ducking.json
        //   - Aplicar al IAudioEngine vía ApplyDuckingConfig
        //   - Disparar OnPropertyChanged para que la UI se actualice
        Ducking.IsEnabled = !Ducking.IsEnabled;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Emergencia (botón en la toolbar)
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// True si el watchdog de emergencia está armado (esperando un silencio
    /// prolongado para dispararse). NO significa que la emergencia esté
    /// sonando ahora — eso es <see cref="IsEmergencyActive"/>.
    /// -------------
    public bool IsEmergencyArmed => _emergencySettings.IsEnabled;

    /// -------------Tooltip dinámico según el estado del botón.</summary>
    public string EmergencyButtonTooltip
    {
        get
        {
            if (IsEmergencyActive) return LocalizationService.GetString("Loc.Main.Emergency.Tooltip.Active");
            if (IsEmergencyArmed) return LocalizationService.GetString("Loc.Main.Emergency.Tooltip.Deactivate");
            return LocalizationService.GetString("Loc.Main.Emergency.Tooltip.Activate");
        }
    }

    /// -------------
    /// Fondo del botón según 3 estados:
    ///   - Disparado (IsEmergencyActive): rojo #DC2626
    ///   - Armado: verde #16A34A
    ///   - Apagado: color base del tema
    /// -------------
    public IBrush EmergencyButtonBackground
    {
        get
        {
            if (IsEmergencyActive) return new SolidColorBrush(Color.Parse("#DC2626"));
            if (IsEmergencyArmed) return new SolidColorBrush(Color.Parse("#16A34A"));
            return ResolveThemeBrush("BgPrimary") ?? new SolidColorBrush(Colors.Transparent);
        }
    }

    /// -------------
    /// Color del ícono: blanco cuando está armado o disparado (para
    /// contrastar con el fondo rojo/verde), color primario si está apagado.
    /// -------------
    public IBrush EmergencyButtonForeground
    {
        get
        {
            if (IsEmergencyActive || IsEmergencyArmed) return new SolidColorBrush(Colors.White);
            return ResolveThemeBrush("FgPrimary") ?? new SolidColorBrush(Colors.Gray);
        }
    }

    /// -------------Evento que el code-behind usa para abrir Config → Emergencia.</summary>
    public event EventHandler? OpenEmergencySettingsRequested;

    /// -------------
    /// Evento que el code-behind usa para mostrar el diálogo de confirmación
    /// al desarmar mientras la emergencia está sonando. El callback recibe
    /// true si el usuario confirma detener, false si cancela.
    /// -------------
    public event EventHandler<Action<bool>>? ConfirmStopEmergencyRequested;

    [RelayCommand]
    private void ToggleEmergency()
    {
        // Caso 1: emergencia sonando → pedir confirmación antes de detener.
        if (IsEmergencyActive)
        {
            ConfirmStopEmergencyRequested?.Invoke(this, (confirmed) =>
            {
                if (confirmed) ApplyEmergencyStop();
            });
            return;
        }

        // Caso 2: armada → desarmar directamente (no hay audio sonando).
        if (IsEmergencyArmed)
        {
            // Delegar TODO al setter del panel: modifica _settings,
            // guarda en disco, dispara SettingsChanged y actualiza el
            // watchdog. NO tocar _emergencySettings directamente para
            // no romper el early-return del setter (ver ApplyEmergencyArm).
            Emergency.IsEnabled = false;
            NotifyEmergencyButtonChanged();
            SetLocalizedStatus("Loc.Main.Status.EmergencyDisabled");
            return;
        }

        // Caso 3: apagada → intentar armar.
        if (!_emergency.IsConfigurationValid())
        {
            // Config incompleta: abrir ventana de configuración. El code-behind
            // llamará a OnEmergencySettingsClosed() cuando el usuario cierre.
            SetLocalizedStatus("Loc.Main.Status.ConfigureEmergencyFirst");
            OpenEmergencySettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ApplyEmergencyArm();
    }

    /// -------------
    /// Llamado por el code-behind después de que el usuario cierra la ventana
    /// de Configuración abierta desde el botón de emergencia.
    ///
    /// Si la configuración quedó válida, arma automáticamente. Si no, el
    /// botón queda apagado y muestra un mensaje en la barra de estado.
    /// -------------
    public void OnEmergencySettingsClosed()
    {
        if (_emergency.IsConfigurationValid())
        {
            ApplyEmergencyArm();
        }
        else
        {
            SetLocalizedStatus("Loc.Main.Status.EmergencyConfigurationIncomplete");
        }
    }

    /// -------------
    /// Detiene la emergencia y la desarma. Llamado desde el code-behind
    /// tras confirmación del usuario, o desde el botón "DETENER" del banner.
    /// -------------
    public void ApplyEmergencyStop()
    {
        try { _emergency.StopEmergency(); } catch { }

        // Delegar el desarme al setter del panel (mismo motivo que
        // ApplyEmergencyArm): garantiza que _emergency.UpdateSettings
        // se ejecute y el watchdog quede correctamente desactivado.
        Emergency.IsEnabled = false;

        NotifyEmergencyButtonChanged();
        SetLocalizedStatus("Loc.Main.Status.EmergencyStopped");
    }

    private void ApplyEmergencyArm()
    {
        // NO tocar _emergencySettings.IsEnabled directamente.
        // Si lo hacemos, el setter del panel ve el valor ya igual y hace
        // early-return, saltándose Save() + SettingsChanged, que es lo que
        // finalmente llama a _emergency.UpdateSettings y arranca el
        // watchdog. Delegar TODO al setter es la única forma limpia.
        Emergency.IsEnabled = true;

        NotifyEmergencyButtonChanged();
        SetLocalizedStatus("Loc.Main.Status.EmergencyEnabled");
    }

    private void NotifyEmergencyButtonChanged()
    {
        OnPropertyChanged(nameof(IsEmergencyArmed));
        OnPropertyChanged(nameof(EmergencyButtonTooltip));
        OnPropertyChanged(nameof(EmergencyButtonBackground));
        OnPropertyChanged(nameof(EmergencyButtonForeground));
    }

    // ───── Dispose ───────────────────────────────────────────────────────
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentTip));
        PlaybackLog.ReloadSnapshot();
        TabHost.RefreshLocalizedTitles();
        TabHostCenter.RefreshLocalizedTitles();
        TabHostRight.RefreshLocalizedTitles();
        OnPropertyChanged(nameof(AlAireText));
        OnPropertyChanged(nameof(PlaybackStateText));
        OnPropertyChanged(nameof(EmergencyButtonTooltip));
        OnPropertyChanged(nameof(AutoDjButtonTooltip));
        OnPropertyChanged(nameof(DuckingTooltip));
        UpdateSpectrumChannel();
        LocutionPanel?.RefreshLocalizedTexts();
        if (!string.IsNullOrEmpty(_autoDjStatusResourceKey))
            SetAutoDjStatus(_autoDjStatusResourceKey, _autoDjStatusResourceArgs);
        if (!string.IsNullOrEmpty(_statusMessageResourceKey))
            SetLocalizedStatus(_statusMessageResourceKey, _statusMessageResourceArgs);
    }

    public void SetLocalizedStatus(string resourceKey, params object?[] args)
    {
        _statusMessageResourceKey = resourceKey;
        _statusMessageResourceArgs = args;
        _settingLocalizedStatus = true;
        try { StatusMessage = string.Format(LocalizationService.GetString(resourceKey), args); }
        finally { _settingLocalizedStatus = false; }
    }

    private void SetAutoDjStatus(string resourceKey, params object?[] args)
    {
        _autoDjStatusResourceKey = resourceKey;
        _autoDjStatusResourceArgs = args;
        _settingLocalizedAutoDjStatus = true;
        try { AutoDjStatusText = string.Format(LocalizationService.GetString(resourceKey), args); }
        finally { _settingLocalizedAutoDjStatus = false; }
        if (_autoDjEditorVm is not null)
            _autoDjEditorVm.RefreshAutoDjStatus(resourceKey, args);
    }

    partial void OnAutoDjStatusTextChanged(string value)
    {
        if (_settingLocalizedAutoDjStatus) return;
        _autoDjStatusResourceKey = null;
        _autoDjStatusResourceArgs = Array.Empty<object?>();
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (_settingLocalizedStatus) return;
        _statusMessageResourceKey = null;
        _statusMessageResourceArgs = Array.Empty<object?>();
    }

    public void Dispose()
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        try { Scheduler.Dispose(); } catch { }
        try { _autoDjTimer.Stop(); } catch { }
        try { _autoDjPlayer.StateChanged -= OnAutoDjPlayerStateChanged; } catch { }
        try { StopAutoDj(); } catch { }
        try { _autoDjPlayer.Dispose(); } catch { }

        // Presentación inicial
        try
        {
            if (Demo is not null)
                Demo.PropertyChanged -= OnDemoPropertyChanged;
        }
        catch { }
        try { Demo?.Dispose(); } catch { }
        try { _demoService?.Dispose(); } catch { }

        try { _emergency?.Dispose(); } catch { }
        try { _tipTimer?.Stop(); } catch { }

        try
        {
            foreach (var p in _extraCartPlayers)
            {
                try { p.Dispose(); } catch { }
            }
            _extraCartPlayers.Clear();
        }
        catch { }

        // Liberar el panel de locución (detiene timers, cancela operaciones).
        try { LocutionPanel?.Dispose(); } catch { }

        // Liberar el motor de locución de RMVoice.
        try { _locution?.Dispose(); } catch { }
    }

    partial void OnPlaybackStateChanged(PlaybackState value) =>
        OnPropertyChanged(nameof(PlaybackStateText));
    partial void OnPositionChanged(TimeSpan value) => OnPropertyChanged(nameof(PositionText));
    partial void OnDurationChanged(TimeSpan value) => OnPropertyChanged(nameof(PositionText));
    partial void OnLastErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnCurrentFilePathChanged(string value) => OnPropertyChanged(nameof(HasFile));

    partial void OnIsNowPlayingActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(NowPlayingPanelBackground));
        OnPropertyChanged(nameof(NowPlayingPanelBorderBrush));
        OnPropertyChanged(nameof(NowPlayingPanelForeground));
    }

    partial void OnIsNextPlayingActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(NextPlayingPanelBackground));
        OnPropertyChanged(nameof(NextPlayingPanelBorderBrush));
        OnPropertyChanged(nameof(NextPlayingPanelForeground));
    }
}
