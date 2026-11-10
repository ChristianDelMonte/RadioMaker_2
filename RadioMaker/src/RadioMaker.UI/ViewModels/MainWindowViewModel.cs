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
using RadioMaker.UI.Services;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _topPanelTimer;
    private readonly DispatcherTimer _tipTimer;

    private IAudioChannel? _currentChannel;

    // ───── Log ───────────────────────────────────────────────────────────
    private readonly LogService _logService;

    // ───── Scheduler + PlaybackEngine + Configurador ─────────────────────
    private readonly SchedulerService _scheduler;
    private readonly PlaybackEngine _playback;
    private readonly ChannelConfigurator _configurator;
    private readonly PlaylistPlayer _playlistPlayer;
    private readonly CartBoardPlayer _cartPlayer;
    private readonly BusExclusivityCoordinator _coordinator;
    private readonly EmergencyWatchdogService _emergency;
    private readonly EmergencySettings _emergencySettings;

    public BusExclusivityCoordinator Coordinator => _coordinator;

    /// <summary>
    /// Motor de audio compartido. Expuesto publicamente para evitar
    /// reflection en la UI (por ejemplo en MainWindow.axaml.cs).
    /// </summary>
    public IAudioEngine AudioEngine => _engine;

    // ───── Locución (RMVoice engine) ─────────────────────────────────────
    /// <summary>
    /// Servicio de locución de RMVoice. Se inicializa despues del motor
    /// de audio. Si RMVoice no esta disponible, queda en modo "no op" y
    /// la app sigue funcionando normalmente.
    /// </summary>
    private readonly LocutionService _locution;

    /// <summary>Configuracion persistente del sistema de locución.</summary>
    private readonly LocutionSettings _locutionSettings;

    // ───── Carpetas favoritas (panel derecho) ────────────────────────────
    /// <summary>
    /// Registry central de carpetas favoritas. Se usa desde el panel
    /// derecho (menu "+") y desde el context menu del arbol del explorador.
    /// </summary>
    public FavoriteFolderRegistry FavoriteFolders { get; }

    // ───── Routing de buses por modulo ───────────────────────────────────
    private readonly ModuleRoutingService _routing;

    // ===== INTEGRACIÓN EXCLUSIVIDAD =====
    /// <summary>
    /// Acceso público al servicio de ruteo (para que la UI de configuración
    /// pueda modificar el AirBusId).
    /// </summary>
    public ModuleRoutingService Routing => _routing;

    public ModuleBusSelectorViewModel BusSelectorCart { get; }
    public ModuleBusSelectorViewModel BusSelectorPlaylist { get; }
    public ModuleBusSelectorViewModel BusSelectorScheduler { get; }
    public ModuleBusSelectorViewModel BusSelectorFileManager { get; }

    /// <summary>
    /// Toggle del modo "un solo bus para todo". Bindea al panel de
    /// configuracion de audio y a la toolbar.
    /// </summary>
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

    // ───── Archivo / reproduccion ────────────────────────────────────────
    [ObservableProperty] private string _currentFilePath = string.Empty;
    [ObservableProperty] private string _statusMessage = "Listo.";
    /// <summary>Tip de ayuda actual mostrado en la barra de estado inferior.</summary>
    [ObservableProperty] private AppTip _currentTip = AppTips.GetRandom();

    /// <summary>Índice del tip actual (para avanzar secuencialmente).</summary>
    private int _tipIndex;
    [ObservableProperty] private string _lastError = string.Empty;
    [ObservableProperty] private PlaybackState _playbackState = PlaybackState.Stopped;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private double _volume = 1.0;

    // ───── Info de la barra de titulo ────────────────────────────────────

    /// <summary>Nombre de la aplicacion que se muestra en la barra de titulo.</summary>
    public string AppName => "RadioMaker";

    /// <summary>Version de la aplicacion (auto-generada en cada build).</summary>
    public string AppVersionText => Services.AppVersionInfo.VersionText;

    /// <summary>Fecha de compilacion (auto-generada en cada build).</summary>
    public string AppBuildText => Services.AppVersionInfo.BuildText;

    /// <summary>Eslogan de la aplicacion.</summary>
    public string AppSlogan => "Forever FREE Radio Automation Software!";

    // ───── Indicador "AL AIRE" ───────────────────────────────────────────

    /// <summary>
    /// True si hay algo reproduciendo en el bus del Scheduler.
    /// </summary>
    public bool IsAlAire => _isAlAire;

    /// <summary>Fondo del panel "AL AIRE" (rojo translucido o transparente).</summary>
    public IBrush AlAireBackground => _isAlAire
        ? new SolidColorBrush(Color.Parse("#33EF4444"))   // rojo translucido
        : new SolidColorBrush(Colors.Transparent);

    /// <summary>Borde del panel "AL AIRE".</summary>
    public IBrush AlAireBorderBrush => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))
        : (ResolveThemeBrush("BorderPrimary") ?? new SolidColorBrush(Color.Parse("#2A2A35")));

    /// <summary>Color del LED indicador.</summary>
    public IBrush AlAireLedColor => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))     // rojo brillante
        : new SolidColorBrush(Color.Parse("#6B7280"));    // gris apagado

    /// <summary>Color del texto del indicador.</summary>
    public IBrush AlAireTextColor => _isAlAire
        ? new SolidColorBrush(Color.Parse("#EF4444"))
        : (ResolveThemeBrush("FgMuted") ?? new SolidColorBrush(Color.Parse("#6B7280")));

    /// <summary>Texto del indicador.</summary>
    public string AlAireText => _isAlAire ? "AL AIRE" : "FUERA DEL AIRE";


    // ───── Panel superior informativo ────────────────────────────────────

    /// <summary>Titulo del audio en reproduccion (scheduler o playlist).</summary>
    [ObservableProperty] private string _nowPlayingTitle = string.Empty;

    /// <summary>Artista u origen del audio en reproduccion.</summary>
    [ObservableProperty] private string _nowPlayingArtist = string.Empty;

    /// <summary>
    /// Canal activo del panel "En reproduccion" (scheduler > playlist > cart).
    /// Es el canal del que se obtiene el FFT para el mini visualizador.
    /// Se actualiza desde RefreshNowPlayingPanel.
    /// </summary>
    [ObservableProperty] private IAudioChannel? _nowPlayingChannel;

    /// <summary>
    /// True si el panel "En reproduccion" tiene contenido (hay algo sonando
    /// en scheduler o playlist). Controla el color cyan activo del panel.
    /// </summary>
    [ObservableProperty] private bool _isNowPlayingActive;

    /// <summary>
    /// True si el panel "Siguiente" tiene contenido real (no es "(sin proximo)").
    /// Controla el color cyan activo del panel.
    /// </summary>
    [ObservableProperty] private bool _isNextPlayingActive;

    /// <summary>Posicion dentro de la lista (ej: "Pista 3 de 8") o vacio.</summary>
    [ObservableProperty] private string _nowPlayingPosition = string.Empty;

    /// <summary>Titulo del proximo item a reproducir, o "(sin proximo)".</summary>
    [ObservableProperty] private string _nextTitle = "(sin proximo)";

    /// <summary>Artista u origen del proximo item.</summary>
    [ObservableProperty] private string _nextArtist = string.Empty;

    /// <summary>Posicion del proximo item.</summary>
    [ObservableProperty] private string _nextPosition = string.Empty;

    // ───── Brushes de los paneles "En reproduccion" / "Siguiente" ────────
    //
    // Se resuelven en el VM (no en el XAML) porque los converters
    // BoolToThemeBrushConverter solo se ejecutan cuando el booleano cambia,
    // no cuando cambia el tema. Notificamos estas propiedades desde el
    // handler de ThemeService.ThemeChanged para que se actualicen en vivo.

    /// <summary>Fondo del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelBackground =>
        ResolveThemeBrush(IsNowPlayingActive ? "PanelLedActiveBg" : "PanelLedInactiveBg");

    /// <summary>Borde del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelBorderBrush =>
        ResolveThemeBrush(IsNowPlayingActive ? "AccentSoft" : "BorderPrimary");

    /// <summary>Color de texto del panel "En reproduccion".</summary>
    public IBrush NowPlayingPanelForeground =>
        ResolveThemeBrush(IsNowPlayingActive ? "LedActive" : "LedInactive");

    /// <summary>Fondo del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelBackground =>
        ResolveThemeBrush(IsNextPlayingActive ? "PanelLedActiveBg" : "PanelLedInactiveBg");

    /// <summary>Borde del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelBorderBrush =>
        ResolveThemeBrush(IsNextPlayingActive ? "AccentSoft" : "BorderPrimary");

    /// <summary>Color de texto del panel "Siguiente".</summary>
    public IBrush NextPlayingPanelForeground =>
        ResolveThemeBrush(IsNextPlayingActive ? "LedActive" : "LedInactive");

    /// <summary>
    /// Fondo del panel que contiene el VU meter. Theme-aware: casi negro
    /// en modo oscuro, cyan claro en modo claro. Se usa como Background
    /// del Border que envuelve el VuMeter en TopInfoPanel.
    /// </summary>
    public IBrush VuPanelBackground => new SolidColorBrush(
        ThemeService.IsDarkVariant
            ? Color.Parse("#0A0A0C")
            : Color.Parse("#D0EEF5"));

    /// <summary>
    /// True si hay algun audio reproduciendose ahora mismo en cualquier
    /// modulo (scheduler, playlist, cart o canal manual). Controla el
    /// encendido/apagado del LED de tiempo.
    /// </summary>
    [ObservableProperty] private bool _isAnyAudioPlaying;

    /// <summary>Tiempo transcurrido del audio actual (formato HH:MM:SS).</summary>
    [ObservableProperty] private string _elapsedText = "00:00:00";

    /// <summary>Tiempo restante del audio actual (formato -HH:MM:SS).</summary>
    [ObservableProperty] private string _remainingText = "-00:00:00";

    /// <summary>Fecha actual (formato DD-MM-YY, compatible con el LED).</summary>
    [ObservableProperty] private string _dateText = "00-00-00";

    /// <summary>Hora actual (formato HH:MM:SS).</summary>
    [ObservableProperty] private string _timeText = "00:00:00";

    /// <summary>Localidad o provincia para el panel de clima.</summary>
    [ObservableProperty] private string _weatherLocation = "—";
    /// <summary>Info del clima (temperatura / humedad).</summary>
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

    /// <summary>
    /// Servicio de locución de RMVoice. La UI lo usa para:
    ///   - Consultar si esta disponible (habilitar/deshabilitar opciones).
    ///   - Disparar locuciones a demanda (botones rapidos).
    ///   - Refrescar el clima cada X minutos.
    ///   - Leer el ultimo snapshot de clima para mostrarlo en el panel.
    /// </summary>
    public LocutionService Locution => _locution;

    /// <summary>
    /// True si RMVoice esta cargado e inicializado. La UI usa esto para
    /// habilitar/deshabilitar las opciones de locución.
    /// </summary>
    public bool IsLocutionAvailable => _locution.IsAvailable;

    // ───── Paneles hijos ─────────────────────────────────────────────────
    public VuMeterSettings VuSettings { get; }
    public EqualizerSettings EqSettings { get; }
    public SilencePanelSettings SilenceSettings { get; }
    public NormalizationSettings NormSettings { get; }
    /// <summary>
    /// Panel del clima + botones de locución. Se muestra en el TopInfoPanel.
    /// </summary>
    public LocutionPanelViewModel LocutionPanel { get; }

    public EqualizerPanelViewModel Equalizer { get; }
    public SilencePanelViewModel Silence { get; }
    public NormalizationPanelViewModel Normalization { get; }
    public FadePanelViewModel Fade { get; }
    public BehaviorSettingsViewModel Behavior { get; }
    public EmergencyPanelViewModel Emergency { get; }
    public EmergencyWatchdogService EmergencyService => _emergency;
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

    /// <summary>Lista de players de carts extras, para poder disponerlos al cerrar.</summary>
    private readonly List<CartBoardPlayer> _extraCartPlayers = new();

    /// <summary>
    /// Lista de players de playlist activos (uno por pestaña). Se usan
    /// para poder aplicar cambios de config en vivo y liberar al cerrar
    /// la pestaña. (FASE B)
    /// </summary>
    private readonly List<PlaylistPlayer> _playlistPlayers = new();

    // ───── Propiedades derivadas ─────────────────────────────────────────
    public string PositionText => $"{Position:mm\\:ss\\.f} / {Duration:mm\\:ss\\.f}";
    public string VolumeText => Volume.ToString("P0");
    public IReadOnlyList<string> SupportedExtensions => _engine.SupportedExtensions;

    // ───── Pestaña activa y qué se puede guardar ────────────────────────

    /// <summary>
    /// Pestaña activa del ultimo TabHost que recibio foco. Puede ser null
    /// si no hay ninguna pestaña seleccionada.
    /// </summary>
    public TabItemViewModel? ActiveTab => _lastActiveTabHost?.SelectedTab;

    public bool CanSaveTanda => ActiveTab?.Content is TandaEditorViewModel;
    public bool CanSaveScheduledList => ActiveTab?.Content is ScheduledListViewModel;
    public bool CanSavePlaylist => ActiveTab?.Content is PlaylistEditorViewModel;
    public bool CanSaveCart => ActiveTab?.Content is CartBoardViewModel;
    public bool CanSaveNotes => ActiveTab?.Content is NotesEditorViewModel;

    /// <summary>
    /// Notifica todas las propiedades relacionadas con la pestaña activa.
    /// Se llama cuando cambia la seleccion en cualquier TabHost.
    /// </summary>
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

    /// <summary>
    /// Cambia la suscripcion al VM de contenido del tab activo. Se llama
    /// cada vez que cambia la pestaña seleccionada en cualquier TabHost.
    /// </summary>
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

    /// <summary>
    /// True si la pestaña activa es un Programado con un item de tipo Bloque
    /// seleccionado. Habilita "Editar > Bloque...".
    /// </summary>
    public bool CanEditBlock =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Block;

    /// <summary>
    /// True si la pestaña activa es un Programado con un item de tipo Tanda
    /// seleccionado. Habilita "Editar > Tanda...".
    /// </summary>
    public bool CanEditTandaItem =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Tanda;

    /// <summary>
    /// True si la pestaña activa es un Programado con un item de tipo Playlist
    /// seleccionado. Habilita "Editar > Playlist...".
    /// </summary>
    public bool CanEditPlaylistItem =>
        ActiveTab?.Content is ScheduledListViewModel list
        && list.SelectedItem?.Kind == ScheduledItemKind.Playlist;

    /// <summary>
    /// True si hay un archivo de audio seleccionado en la pestaña activa
    /// Habilita "Editar tags...".
    /// </summary>
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

    /// <summary>
    /// Devuelve la ruta del archivo de audio seleccionado en la pestaña
    /// activa, o null si no hay ninguno. Excluye el File Manager.
    /// </summary>
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
        PlaybackState.Playing => "Reproduciendo",
        PlaybackState.Paused  => "Pausado",
        PlaybackState.Stopped => "Detenido",
        PlaybackState.Ended   => "Fin de reproduccion",
        PlaybackState.Stalled => "Estancado",
        PlaybackState.Error   => "Error",
        _ => PlaybackState.ToString()
    };

    /// <summary>
    /// Indica si el tema efectivo actual es oscuro. Se usa para elegir
    /// el icono del boton rapido de tema (sol cuando esta oscuro, luna
    /// cuando esta claro).
    /// </summary>
    public bool IsDarkTheme => ThemeService.IsDarkVariant;
    public bool IsLightTheme => !ThemeService.IsDarkVariant;
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();

    /// <summary>
    /// Indica si la ventana principal esta maximizada. Se usa para elegir
    /// el icono del boton maximizar/restaurar en la barra de titulo.
    /// </summary>
    [ObservableProperty] private bool _isMaximized;

    public bool HasError => !string.IsNullOrEmpty(LastError);
    public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath);
    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public bool CanPlay => HasFile && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanStop => HasFile && PlaybackState != PlaybackState.Stopped;
    public bool HasBuses => Buses.Count > 0;

    /// <summary>
    /// Ultimo TabHost que recibio foco (cambio su SelectedTab). Se usa
    /// para saber cual es la "pestaña activa" cuando el menu Guardar
    /// tiene que decidir que opcion habilitar.
    /// </summary>
    private TabHostViewModel? _lastActiveTabHost;

    /// <summary>
    /// VM de contenido del tab activo. Nos suscribimos a su PropertyChanged
    /// para detectar cambios de seleccion internos (SelectedItem, SelectedTrack,
    /// SelectedButton) y refrescar las propiedades de los menus Editar/Guardar.
    /// </summary>
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

            // Cart principal + carts extras.
            var cart = GetAnyActiveCartChannel();
            if (cart is not null) return cart;

            return null;
        }
    }

    /// <summary>
    /// Devuelve el primer canal activo entre el cart principal y todos
    /// los carts extras (botoneras adicionales). Devuelve null si
    /// ninguno esta reproduciendo.
    /// </summary>
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

    /// <summary>
    /// True si el cart principal o algun cart extra esta reproduciendo.
    /// </summary>
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
        _locutionSettings = LocutionSettingsStore.Load();

        // 5) PlaybackEngine.
        _playback = new PlaybackEngine(
            _engine,
            _scheduler,
            action => Dispatcher.UIThread.Post(action),
            _coordinator,
            _locution);

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
            _coordinator);

        _playlistPlayer.ConfigureChannel = (channel, filePath, logDetails) =>
            _configurator.ConfigureChannel(channel, filePath, "PLAYLIST", logDetails);

        _playlistPlayer.PrewarmFile = path => _configurator.Prewarm(path);

        // 7) ViewModels de paneles.
        Equalizer = new EqualizerPanelViewModel(EqSettings);
        Silence = new SilencePanelViewModel(SilenceSettings);
        Normalization = new NormalizationPanelViewModel(NormSettings);
        Fade = new FadePanelViewModel();
        Behavior = new BehaviorSettingsViewModel();
        Behavior.BehaviorChanged = ApplyInterruptionBehaviorToAllPlaylistPlayers;

        // ===== EMERGENCIA =====
        _emergencySettings = EmergencySettingsStore.Load();

        _emergency = new EmergencyWatchdogService(
            _engine,
            _coordinator,
            getAirBusId: () => _routing?.AirBusId ?? string.Empty,
            dispatch: action => Dispatcher.UIThread.Post(action),
            settings: _emergencySettings);

        _emergency.ConfigureChannel = (ch, path, log) =>
            _configurator.ConfigureChannel(ch, path, "EMERGENCY", log);

        Emergency = new EmergencyPanelViewModel(_emergencySettings, _emergency);
        Emergency.SettingsChanged = (s) => _emergency.UpdateSettings(s);

        _emergency.EmergencyStarted += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsEmergencyActive));
            OnPropertyChanged(nameof(EmergencyStatusText));
        });
        _emergency.EmergencyStopped += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsEmergencyActive));
            OnPropertyChanged(nameof(EmergencyStatusText));
        });
        _emergency.NowPlayingChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(EmergencyStatusText));
        });

        Behavior.EmergencyPanel = Emergency;
        Ducking = new DuckingPanelViewModel(DuckingSettingsStore.Load(), engine);
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
            playbackEngine: _playback)
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
            playbackEngine: _playback)
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
                _logService.Debug("Fade", $"Auto-fade: {Fade.IsEnabled} @ {Fade.Seconds}s");
            }
            else if (e.PropertyName == nameof(FadePanelViewModel.CartCrossfadeEnabled) ||
                     e.PropertyName == nameof(FadePanelViewModel.CartCrossfadeSeconds))
            {
                ApplyCrossfadeToCartPlayers();
                ApplyCrossfadeToPlaylistPlayers();

                // Sincronizar la duracion del crossfade del coordinator.
                _coordinator.CrossfadeDurationMs =
                    (int)Math.Round(Fade.CartCrossfadeSeconds * 1000.0);

                _logService.Debug("Cart",
                    $"Crossfade: {Fade.CartCrossfadeEnabled} @ {Fade.CartCrossfadeSeconds:F1}s");
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
        catch { }

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
            LastError = $"No se pudo iniciar el scheduler: {ex.Message}";
            _logService.Error("Scheduler", "No se pudo iniciar", ex);
        }
    }

    private void InitializeEngine()
    {
        try
        {
            _engine.Initialize(deviceIndex: -1, sampleRate: 44100);
            ReloadBuses();

            // Aplicar el volumen actual del VM al engine (por si arranca en <1).
            try { _engine.GlobalVolume = Volume; } catch { }

            StatusMessage = $"Motor inicializado. {Buses.Count} bus(es) activo(s).";
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
            LastError = $"Error al inicializar el motor de audio: {ex.Message}";
            StatusMessage = "Fallo en la inicializacion del motor.";
            _logService.Error("Engine", "Fallo al inicializar", ex);
        }
    }

    /// <summary>
    /// Devuelve TODOS los canales activos de la app (de todos los
    /// reproductores). Se usa para el ducking de locuciones disparadas
    /// desde el panel de clima/hora.
    /// </summary>
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

        // 3) Canales de TODAS las playlists (una por pestaña).
        // ── FASE B
        foreach (var p in _playlistPlayers)
        {
            try { list.AddRange(p.GetActiveChannels()); } catch { }
        }

        // 4) Canales de la botonera principal.
        try { list.AddRange(_cartPlayer.GetActiveChannels()); } catch { }

        // 5) Canales de las botoneras extras.
        foreach (var p in _extraCartPlayers)
        {
            try { list.AddRange(p.GetActiveChannels()); } catch { }
        }

        return list;
    }

    /// <summary>
    /// Inicializa el servicio de locución de RMVoice. Es seguro llamarlo
    /// aunque RMVoice no esté disponible: en ese caso, deja el servicio
    /// en modo "no op" y sigue sin afectar a la app.
    /// </summary>
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
                StatusMessage = "El bus de reproduccion fue eliminado. Volve a cargar un archivo.";
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
            StatusMessage = $"PROGRAMADO: {e.Item.DisplayTitle} desde '{e.SourceListName}'";
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

            StatusMessage = "Estado de disparos reiniciado.";
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
            StatusMessage = "No hay una pestaña activa.";
            return;
        }

        try
        {
            switch (selectedTab.Content)
            {
                case PlaylistEditorViewModel playlist:
                    playlist.AddTrackFromFile(file.FullPath);
                    StatusMessage = $"Agregado a Playlist: {file.FileName}";
                    _logService.Info("FileBrowser", $"Agregado a playlist: {file.FileName}");
                    break;

                case ScheduledListViewModel scheduled:
                    scheduled.AddFileAsItem(file.FullPath);
                    StatusMessage = $"Agregado a Programados: {file.FileName}";
                    _logService.Info("FileBrowser", $"Agregado a programados: {file.FileName}");
                    break;

                case TandaEditorViewModel tanda:
                    tanda.AddTrackFromFile(file.FullPath);
                    StatusMessage = $"Agregado a Tanda: {file.FileName}";
                    _logService.Info("FileBrowser", $"Agregado a tanda: {file.FileName}");
                    break;

                default:
                    StatusMessage = $"La pestaña '{selectedTab.Title}' no admite archivos.";
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al agregar: {ex.Message}";
            _logService.Error("FileBrowser", "Error al agregar archivo", ex);
        }
    }

    /// <summary>
    /// Crea un CartBoard extra con su propio CartBoardPlayer independiente.
    /// El nuevo cart arranca vacio (sin archivo). Si el usuario lo quiere
    /// persistir, tiene que guardarlo con "Guardar como...".
    /// </summary>
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

    /// <summary>
    /// Devuelve el bus del modulo Cart (el default de las botoneras).
    /// Si el routing no esta disponible, cae al primario.
    /// </summary>
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

    /// <summary>
    /// Crea un nuevo editor de playlist con su PROPIO PlaylistPlayer y su
    /// PROPIO SimpleBusSelector. Se llama cada vez que se abre una nueva
    /// pestaña de playlist.
    ///
    /// El bus inicial es el del modulo Scheduler (el "aire"). Si la
    /// playlist tiene un OutputBusId persistido, se restaura al cargar
    /// el archivo. (FASE B)
    /// </summary>
    private PlaylistEditorViewModel CreatePlaylistEditorForTab()
    {
        // 1) Crear un player nuevo.
        var player = new PlaylistPlayer(
            _engine,
            action => Dispatcher.UIThread.Post(action),
            _locution,
            _coordinator);

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

    /// <summary>
    /// Libera los recursos de un editor de playlist al cerrar su pestaña.
    /// </summary>
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

    /// <summary>
    /// Devuelve el bus del modulo Scheduler (el "aire"). Si no hay
    /// routing disponible, cae al primario.
    /// </summary>
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

    /// <summary>
    /// Handler comun a todos los players de playlist. Se usa para
    /// coordinar la coexistencia con el Scheduler.
    /// </summary>
    private void OnAnyPlaylistPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HandlePlaylistCoexistence(sender as PlaylistPlayer, state);
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
        foreach (var p in _playlistPlayers)
        {
            try { if (p.IsPlayingNow) return true; } catch { }
        }
        return false;
    }

    /// <summary>
    /// Libera los recursos de un CartBoard extra al cerrar su pestaña.
    /// </summary>
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

    /// <summary>
    /// Detiene toda la reproduccion en curso: botones del cart, playlist
    /// activa, scheduler y canal manual. Se invoca desde el menu
    /// "Reproduccion -> Detener todo".
    /// </summary>
    public void StopAllPlayback()
    {
        try { _cartPlayer.StopAll(); } catch { }
        try { _playlistPlayer.Stop(); } catch { }
        try { _playback.StopAll(); } catch { }

        // Canal manual (boton "Examinar").
        try { _currentChannel?.Stop(); } catch { }

        // Detener los timers de UI.
        _positionTimer.Stop();
        _meterTimer.Stop();
        ResetMeter();

        StatusMessage = "Reproduccion detenida globalmente.";
        _logService.Info("APP", "Detener todo ejecutado (cart + playlist + scheduler + manual).");

        RefreshCommandStates();
    }

    /// <summary>
    /// Handler comun para los cambios de configuracion de EQ, Trim y
    /// Normalizacion. Reune todos los canales activos de los 4 reproductores
    /// (manual, scheduler, playlist, cart) y les reaplica la configuracion.
    ///
    /// Esto resuelve el bug donde cambiar el EQ no tenia efecto sobre audio
    /// que estaba sonando desde el cart, el scheduler o una playlist.
    /// </summary>
    private void OnProcessingSettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            // Reunir todos los canales activos.
            var channels = new List<IAudioChannel?>();

            // 1) Canal manual (boton "Examinar..." del panel central).
            channels.Add(_currentChannel);

            // 2) Canal del scheduler (PlaybackEngine).
            try { channels.AddRange(_playback.GetActiveChannels()); } catch { }

            // 3) Canales de la playlist (principal + mixado).
            try { channels.AddRange(_playlistPlayer.GetActiveChannels()); } catch { }

            // 4) Canales de la botonera (todos los botones sonando).
            try { channels.AddRange(_cartPlayer.GetActiveChannels()); } catch { }

            // Reaplicar la configuracion a todos.
            _configurator.ReapplyToAll(channels);
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
        foreach (var p in _playlistPlayers)
        {
            try { p.SetCrossfadeSeconds(sec); } catch { }
        }
    }

    /// <summary>
    /// Aplica el estado y la duracion del crossfade al cart player
    /// principal y a todos los cart players extras (botoneras adicionales).
    /// Se llama al arrancar y cada vez que el usuario cambia el valor en
    /// el panel de configuracion.
    /// </summary>
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

    /// <summary>
    /// Propaga el comportamiento de interrupcion a TODOS los players de
    /// playlist (legacy + FASE B). Se llama cuando el usuario cambia la
    /// setting en el panel de Scheduler.
    /// </summary>
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

    /// <summary>
    /// Alterna el tema entre claro y oscuro.
    ///
    /// Reglas:
    ///   - Si esta en Dark explicito -> pasa a Light explicito.
    ///   - Si esta en Light explicito -> pasa a Dark explicito.
    ///   - Si esta en System -> pasa al opuesto del tema efectivo actual
    ///     (si el SO esta en oscuro, pasa a Light explicito).
    /// </summary>
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
        try { _emergency.StopEmergency(); } catch { }
    }

    [RelayCommand] private void Play()
    {
        try
        {
            if (_currentChannel is null) return;
            _currentChannel.Play();
            _positionTimer.Start();
            _meterTimer.Start();
            StatusMessage = "Reproduciendo.";
            _logService.Info("Player", "Reproduciendo manual.");
            LastError = string.Empty;
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
            StatusMessage = "Pausado.";
            RefreshCommandStates();
        }
        catch (Exception ex) { LastError = ex.Message; }
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
            StatusMessage = "Detenido.";
            RefreshCommandStates();
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public void LoadFile(string filePath)
    {
        try
        {
            if (SelectedBus is null) { LastError = "No hay bus de salida."; return; }

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
            StatusMessage = $"Cargado: {Path.GetFileName(filePath)}";
            _logService.Info("Player", $"Cargado: {Path.GetFileName(filePath)}");
            LastError = string.Empty;
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
            Position = Duration;
            PlaybackState = PlaybackState.Ended;
            StatusMessage = e.WasStoppedByUser ? "Detenido por el usuario." : "Fin.";
            RefreshCommandStates();
        });
    }

    private void OnChannelStateChanged(object? sender, PlaybackState state)
    {
        Dispatcher.UIThread.Post(() => { PlaybackState = state; RefreshCommandStates(); });
    }

    // ═════════════════════════════════════════════════════════════════════
    // Panel superior: LED de tiempo + fecha/hora
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
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
    /// </summary>
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

        // 5) Panel "Siguiente".
        RefreshNextPlayingPanel();

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

    /// <summary>
    /// Salta manualmente al siguiente tip (útil para debug o para que el
    /// operador pueda pedir otro tip si el actual no le interesa).
    /// </summary>
    [RelayCommand]
    private void SkipTip() => AdvanceTip();

    // ═════════════════════════════════════════════════════════════════════
    // Panel "En reproduccion" y "Siguiente"
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Actualiza las propiedades NowPlaying* con la info del canal activo
    /// segun la prioridad scheduler > playlist. Cart y canal manual no
    /// participan de este panel.
    /// </summary>
    private void RefreshNowPlayingPanel()
    {
        try
        {
            // 1) Scheduler
            var schedChannel = _playback.CurrentChannel;
            if (schedChannel is not null && schedChannel.State == PlaybackState.Playing)
            {
                var info = _currentPlaybackInfo;
                var path = _playback.CurrentFilePath;
                var tags = GetTagsCached(path);

                string title;
                string secondary;
                string position;

                if (info.IsTanda)
                {
                    title = !string.IsNullOrWhiteSpace(info.TandaTrackTitle)
                        ? info.TandaTrackTitle
                        : ResolveDisplayTitle(tags, SafeFileName(path));

                    secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : $"Tanda: {info.TandaNameText}";

                    position = info.TandaProgressText;
                }
                else
                {
                    title = ResolveDisplayTitle(tags, SafeFileName(path));

                    secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : (!string.IsNullOrWhiteSpace(info.SourceListName)
                            ? $"Programado: {info.SourceListName}"
                            : "Programado");

                    position = string.Empty;
                }

                NowPlayingTitle = title;
                NowPlayingArtist = secondary;
                NowPlayingPosition = position;
                NowPlayingChannel = schedChannel;
                IsNowPlayingActive = true;
                return;
            }

            // 2) Playlist (cualquier pestaña que este sonando)
            // ── FASE B
            var activePl = GetActivePlaylistPlayer();
            var plChannel = activePl?.CurrentChannel;
            if (activePl is not null && plChannel is not null &&
                plChannel.State == PlaybackState.Playing)
            {
                var path = activePl.CurrentFilePath;
                var tags = GetTagsCached(path);
                var playlist = activePl.CurrentPlaylist;
                int idx = activePl.CurrentIndex;

                string title = ResolveDisplayTitle(tags, SafeFileName(path));
                string secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : (playlist?.DisplayName ?? "Playlist");

                NowPlayingTitle = title;
                NowPlayingArtist = secondary;
                NowPlayingPosition = (playlist is not null && idx >= 0)
                    ? $"Pista {idx + 1} de {playlist.Tracks.Count}"
                    : string.Empty;
                NowPlayingChannel = plChannel;
                IsNowPlayingActive = true;
                return;
            }

            // 3) Cart (SOLO si no hay scheduler ni playlist activos).
            //    El Cart nunca tiene prioridad sobre lo que "sale al aire":
            //    programados, tandas y playlists siempre ganan. Solo se
            //    muestra si es lo unico sonando.
            //    Cubre el cart principal Y los carts extras.
            var cartChannel = GetAnyActiveCartChannel();
            if (cartChannel is not null)
            {
                var path = cartChannel.FilePath;
                var tags = GetTagsCached(path);

                string title = ResolveDisplayTitle(tags, SafeFileName(path));
                string secondary = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : "Cart";

                NowPlayingTitle = title;
                NowPlayingArtist = secondary;
                NowPlayingPosition = string.Empty;
                NowPlayingChannel = cartChannel;
                IsNowPlayingActive = true;
                return;
            }

            // 4) Nada sonando en ningun reproductor (ni manual).
            NowPlayingTitle = string.Empty;
            NowPlayingArtist = string.Empty;
            NowPlayingPosition = string.Empty;
            NowPlayingChannel = null;
            IsNowPlayingActive = false;
            return;
        }
        catch { }

        // Actualizar el flag activo para el cambio de color del panel.
        IsNowPlayingActive = !string.IsNullOrWhiteSpace(NowPlayingTitle);
    }

    /// <summary>
    /// Actualiza las propiedades Next* con el proximo item a reproducir.
    /// Orden de prioridad:
    ///   1. Cola del scheduler (items pendientes).
    ///   2. Item suspendido del scheduler (que se va a reanudar).
    ///   3. Proxima pista de la playlist.
    ///   4. "(sin proximo)".
    /// </summary>
    private void RefreshNextPlayingPanel()
    {
        try
        {
            // ═══════════════════════════════════════════════════════════
            // 0) PRIORIDAD MAXIMA: si estamos DENTRO de una tanda y hay
            //    tracks pendientes, el "siguiente" es el proximo track
            //    de la misma tanda. Esto es lo que se salteaba antes y
            //    hacia que apareciera la playlist suspendida en lugar del
            //    track 2 de 7.
            // ═══════════════════════════════════════════════════════════
            var schedChannel = _playback.CurrentChannel;
            bool schedulerActive = schedChannel is not null &&
                                   schedChannel.State == PlaybackState.Playing;

            if (schedulerActive)
            {
                var nextTanda = _playback.GetNextTandaTrackInfo();
                if (nextTanda is not null)
                {
                    var tags = GetTagsCached(nextTanda.Value.FilePath);

                    NextTitle = ResolveDisplayTitle(tags, nextTanda.Value.Title);

                    NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                        ? tags!.Performers
                        : "tanda en curso";

                    NextPosition = $"Pista {nextTanda.Value.Index + 1} de {nextTanda.Value.Count}";
                    IsNextPlayingActive = true;
                    return;
                }
            }

            // ═══════════════════════════════════════════════════════════
            // 1) Cola del scheduler (items pendientes fuera de la tanda
            //    actual: otros items que se dispararon mientras sonaba algo).
            // ═══════════════════════════════════════════════════════════
            var queue = _playback.GetQueuedItems();
            if (queue.Count > 0)
            {
                var next = queue[0];

                NextTitle = next.Title;

                NextArtist = next.IsTanda && next.TandaTrackCount > 0
                    ? $"Tanda, {next.TandaTrackCount} pista/s"
                    : (!string.IsNullOrWhiteSpace(next.ListName)
                        ? $"desde {next.ListName}"
                        : string.Empty);

                NextPosition = string.Empty;
                IsNextPlayingActive = true;
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // 2) Suspendido del scheduler (por ejemplo, una playlist que
            //    fue interrumpida y se va a reanudar al terminar).
            // ═══════════════════════════════════════════════════════════
            var suspendedTitle = _playback.GetSuspendedItemTitle();
            if (!string.IsNullOrWhiteSpace(suspendedTitle))
            {
                NextTitle = suspendedTitle;

                var tandaInfo = _playback.GetSuspendedTandaInfo();
                if (tandaInfo is not null)
                {
                    NextArtist = tandaInfo.Value.TrackTitle;
                    NextPosition = $"Pista {tandaInfo.Value.Index + 1} de {tandaInfo.Value.Count}";
                }
                else
                {
                    NextArtist = "se reanudara";
                    NextPosition = string.Empty;
                }
                IsNextPlayingActive = true;
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // 3) Proxima pista de la playlist activa.
            // ── FASE B
            // ═══════════════════════════════════════════════════════════
            var activePl = GetActivePlaylistPlayer();
            var upcoming = activePl?.GetUpcomingTrackInfo();
            if (upcoming is not null)
            {
                var tags = GetTagsCached(upcoming.Value.FilePath);

                NextTitle = ResolveDisplayTitle(tags, upcoming.Value.Title);

                NextArtist = !string.IsNullOrWhiteSpace(tags?.Performers)
                    ? tags!.Performers
                    : string.Empty;

                NextPosition = $"Pista {upcoming.Value.Index + 1} de {upcoming.Value.Total}";
                IsNextPlayingActive = true;
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // 4) Nada mas en el horizonte.
            // ═══════════════════════════════════════════════════════════
            NextTitle = "(sin proximo)";
            NextArtist = string.Empty;
            NextPosition = string.Empty;
            IsNextPlayingActive = false;
            return;
        }
        catch { }

        // Actualizar el flag activo para el cambio de color del panel.
        IsNextPlayingActive = !string.IsNullOrWhiteSpace(NextTitle)
            && NextTitle != "(sin proximo)";
    }

    /// <summary>
    /// Devuelve el nombre del archivo sin extension, con un fallback seguro
    /// si el path es vacio o invalido.
    /// </summary>
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

    /// <summary>
    /// Resuelve el titulo a mostrar para un audio, aplicando la cadena
    /// de fallbacks:
    ///   1. Title del TAG (si esta cargado y no vacio).
    ///   2. Fallback provisto (nombre de archivo, titulo de track, etc.).
    ///
    /// Nota: NO usamos "tags?.DisplayTitle ?? fallback" porque DisplayTitle
    /// devuelve string vacio cuando el Title esta vacio (no null), y el
    /// operador ?? solo captura null. Este helper cubre ambos casos.
    /// </summary>
    private static string ResolveDisplayTitle(
        RadioMaker.Core.Tags.AudioTagInfo? tags,
        string? fallback)
    {
        if (tags is not null && !string.IsNullOrWhiteSpace(tags.DisplayTitle))
            return tags.DisplayTitle;

        return fallback ?? string.Empty;
    }

    /// <summary>
    /// Lee los TAGs del path con un cache de 1 entrada. Si el path no cambio
    /// desde la ultima lectura, devuelve el cacheado sin tocar el disco.
    /// </summary>
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

    /// <summary>
    /// Devuelve el canal que debe usarse para el LED de tiempo, aplicando
    /// la prioridad: scheduler > playlist > cart > manual. Devuelve null
    /// si ninguno esta reproduciendo.
    /// </summary>
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

    /// <summary>
    /// Formatea un TimeSpan como HH:MM:SS para el LED (2 digitos por campo,
    /// horas sin limite superior).
    /// </summary>
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

    /// <summary>
    /// Determina cual es el canal activo ahora y lo propaga al panel Spectrum
    /// si cambio respecto al tick anterior. Tambien detecta la etiqueta de
    /// origen (MANUAL / CART / SCHEDULER / PLAYLIST).
    /// </summary>
    private void UpdateSpectrumChannel()
    {
        IAudioChannel? active = null;
        string source = string.Empty;

        if (_currentChannel is not null)
        {
            active = _currentChannel;
            source = "MANUAL";
        }
        else
        {
            var sched = _playback.CurrentChannel;
            if (sched is not null)
            {
                active = sched;
                source = "SCHEDULER";
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
                    source = "PLAYLIST";
                }
                else
                {
                    var cart = GetAnyActiveCartChannel();
                    if (cart is not null)
                    {
                        active = cart;
                        source = "CART";
                    }
                }
            }
        }

        // Solo actualizar si cambio.
        if (ReferenceEquals(active, _lastSpectrumChannel)) return;

        _lastSpectrumChannel = active;
        Spectrum.SetChannel(active, source);
    }

    private void RefreshMeter()
    {
        var ch = ActiveChannel;
        if (ch is null) return;

        AudioLevels levels;
        try { levels = ch.Meter.Read(); }
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

    /// <summary>
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
    /// </summary>
    private void SyncPlaybackState()
    {
        try
        {
            var activeState = ActiveChannel?.State ?? PlaybackState.Stopped;

            // Solo override si estabamos en un estado "activo".
            bool wasActive = PlaybackState == PlaybackState.Playing
                             || PlaybackState == PlaybackState.Paused;

            if (wasActive && PlaybackState != activeState)
            {
                PlaybackState = activeState;
                RefreshCommandStates();
            }
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
    }

    private PlaybackStateKind _lastPlaybackEngineKind = PlaybackStateKind.Idle;
    private IAudioChannel? _lastSpectrumChannel;

    /// <summary>
    /// True si hay algo reproduciendo en el bus del Scheduler (el "aire").
    /// Se actualiza en UpdateTabLiveStates y alimenta el indicador visual
    /// "AL AIRE" del menu principal.
    /// </summary>
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
        try { _engine.GlobalVolume = value; } catch { }

        // Persistir en disco para el proximo arranque.
        try { VolumeSettingsStore.Save(value); } catch { }

        OnPropertyChanged(nameof(VolumeText));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Routing de buses por modulo
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recalcula el bus efectivo de cada modulo y lo aplica al reproductor
    /// correspondiente. Se llama al arrancar, al cambiar el modo global, al
    /// cambiar el bus global, y al agregar/quitar buses.
    /// </summary>
    private void ApplyRoutingToPlayers()
    {
        try { _playback.SetBus(_routing.GetEffectiveBus(AudioModuleKind.Scheduler)); } catch { }

        // ── FASE B / C: las playlists y los carts tienen bus POR PESTAÑA.
        // No aplicamos el routing del modulo a las pestañas existentes.
        // El modulo solo se usa como default al crear pestañas nuevas
        // (via GetCartDefaultBus / GetSchedulerBus).
    }

    /// <summary>
    /// Handler de cambios generales del routing (toggle global, cambio de
    /// bus global). Reaplica todo.
    /// </summary>
    private void OnRoutingChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyRoutingToPlayers();
            OnPropertyChanged(nameof(UseGlobalBus));

            // Si cambio el AirBus, refrescar el indicador inmediatamente.
            try { UpdateTabLiveStates(); } catch { }

            StatusMessage = _routing.UseGlobalBus
                ? $"Bus global aplicado a todos los modulos: {_routing.GlobalBus.DisplayName}"
                : "Modo por modulo activo. Cada modulo puede elegir su salida.";
        });
    }

    /// <summary>
    /// Handler de cambio especifico del bus de un modulo. Solo reaplica
    /// ese modulo, no los otros.
    /// </summary>
    private void OnModuleBusChanged(object? sender, AudioModuleKind kind)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var bus = _routing.GetEffectiveBus(kind);

            switch (kind)
            {
                case AudioModuleKind.Cart:
                    // ── FASE C: el bus del cart es POR PESTAÑA. Este handler
                    // ya no toca pestañas existentes. El modulo Cart solo
                    // se usa como default al crear pestañas nuevas.
                    StatusMessage = $"Bus default de Cart -> {bus.DisplayName}";
                    break;

                case AudioModuleKind.Playlist:
                    // ── FASE B: el bus de playlist es POR PESTAÑA.
                    // Este handler ya no toca pestañas existentes. El
                    // modulo Playlist solo se usa como default al crear
                    // pestañas nuevas.
                    StatusMessage = $"Bus default de Playlist -> {bus.DisplayName}";
                    break;

                case AudioModuleKind.Scheduler:
                    try { _playback.SetBus(bus); } catch { }
                    StatusMessage = $"Programados -> {bus.DisplayName}";
                    break;

                case AudioModuleKind.FileManager:
                    // Reservado para preescucha del File Manager.
                    StatusMessage = $"File Manager -> {bus.DisplayName}";
                    break;
            }
        });
    }

    partial void OnSelectedBusChanged(IAudioBus? oldValue, IAudioBus? newValue)
    {
        if (_suppressBusSelectionHook) return;
        if (ReferenceEquals(oldValue, newValue)) return;
        if (newValue is null) return;

        // El combo global del header ahora actua como "bus global".
        // El routing se encarga de propagar a los modulos segun el modo.
        _routing?.SetGlobalBus(newValue);

        if (HasFile)
        {
            ReleaseCurrentChannel();
            StatusMessage = "Canal liberado al cambiar de bus.";
        }

        RefreshCommandStates();
    }

    // ───── Workspace (persistencia de pestañas) ──────────────────────────

    /// <summary>
    /// Guarda el estado del workspace: recolecta las entradas de las
    /// zonas izquierda y central, y las persiste junto con el indice
    /// activo de cada zona.
    /// </summary>
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
        catch { }
    }

    /// <summary>
    /// Guarda TODAS las botoneras (la principal y las extras) en su
    /// archivo .rcb correspondiente. Se llama desde SaveWorkspace al
    /// cerrar la app para no perder cambios sin guardar.
    ///
    /// Las botoneras que nunca fueron guardadas en un .rcb (extras
    /// nuevas) se guardan en la ruta por defecto SOLO si tienen algun
    /// boton con archivo asignado. Asi no generamos archivos .rcb basura.
    /// </summary>
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
        catch { }

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

                    try { extraCart.SaveToFile(extraCart.LoadedFilePath); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Restaura el workspace: reparte las entradas entre las zonas segun
    /// su campo Zone, y reaplica el indice activo de cada zona.
    /// </summary>
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

    /// <summary>
    /// Resuelve un brush del tema actual a partir de una clave de recurso.
    /// Devuelve un brush transparente si la clave no existe o si no hay
    /// Application activa.
    /// </summary>
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

    /// <summary>
    /// Abre la ventana de configuracion en la seccion "Datos climaticos".
    /// Se dispara desde el menu contextual del panel de clima.
    /// </summary>
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

    /// <summary>
    /// Lanza RMVoice.exe si esta presente en Addons/RMVoice/.
    /// </summary>
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

                StatusMessage = "Abriendo RMVoice...";
                return;
            }

            try
            {
                _logService.Warning("ADDON-LAUNCHER",
                    "No se encontró RMVoice.exe. Verificar que esté en Addons/RMVoice/.");
            }
            catch { }

            StatusMessage = "No se encontro RMVoice.exe. Verifica que este en Addons/RMVoice/.";
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

            StatusMessage = $"Error al abrir RMVoice: {ex.Message}";
        }
    }

    /// <summary>
    /// Evento que el code-behind usa para abrir la ventana de configuracion
    /// en una seccion concreta.
    /// </summary>
    public event EventHandler<string>? OpenSettingsRequested;

    /// <summary>
    /// Devuelve el PlaylistPlayer que este REALMENTE reproduciendo. Si
    /// ninguno esta reproduciendo, devuelve el primero con una playlist
    /// cargada (pausado o preparado). (FASE B)
    /// </summary>
    private PlaylistPlayer? GetActivePlaylistPlayer()
    {
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

    /// <summary>
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
    /// </summary>
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

    /// <summary>
    /// Determina si hay algo reproduciendo en el bus del Scheduler:
    ///   - El PlaybackEngine reproduciendo cualquier item.
    ///   - O una playlist reproduciendo en el bus del Scheduler.
    /// </summary>
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

    /// <summary>
    /// Devuelve el primer canal de playlist activo (el que esta sonando
    /// ahora). Prioriza los que estan reproduciendo por sobre los pausados.
    /// (FASE B)
    /// </summary>
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

    // ───── Dispose ───────────────────────────────────────────────────────
    public void Dispose()
    {
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