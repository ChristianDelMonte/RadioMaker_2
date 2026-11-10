using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Playback;
using RadioMaker.UI.Configuration;
using RMVoice.Core.Models;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del panel de clima / locución del TopInfoPanel.
///
/// Responsabilidades:
///   - Mostrar la ubicación, temperatura y humedad actuales.
///   - Refrescar el clima periódicamente (configurable).
///   - Exponer comandos para reproducir la locución de clima u hora
///     desde los botones del panel.
///   - Emitir eventos para que MainWindowViewModel abra la ventana de
///     configuración o la app RMVoice.
/// </summary>
public sealed partial class LocutionPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly LocutionService _locution;
    private readonly LocutionSettings _settings;

    /// <summary>
    /// Callback para obtener TODOS los canales activos de la app. Lo setea
    /// MainWindowViewModel. Se usa para hacer ducking del audio en curso
    /// mientras suena una locucion disparada desde el panel.
    /// </summary>
    private readonly Func<IEnumerable<IAudioChannel>>? _getActiveChannels;

    private DispatcherTimer? _refreshTimer;
    private CancellationTokenSource? _playCts;
    private bool _disposed;

    // ───── Configuracion persistente ─────────────────────────────────────

    public LocutionSettings Settings => _settings;

    // ───── Buses disponibles para la salida del panel ────────────────────

    /// <summary>
    /// Lista de buses disponibles del engine. Se expone como
    /// ObservableCollection para bindear el ComboBox del panel de config.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<IAudioBus> AvailableBuses { get; }
        = new();

    /// <summary>
    /// Bus seleccionado en el ComboBox. Sincroniza con
    /// <see cref="LocutionSettings.OutputBusId"/>.
    /// </summary>
    public IAudioBus? SelectedBus
    {
        get
        {
            var id = _settings.OutputBusId;
            if (string.IsNullOrEmpty(id)) return AvailableBuses.FirstOrDefault();
            return AvailableBuses.FirstOrDefault(b => b.Id == id)
                   ?? AvailableBuses.FirstOrDefault();
        }
        set
        {
            if (value is null) return;
            if (_settings.OutputBusId == value.Id) return;

            _settings.OutputBusId = value.Id;
            LocutionSettingsStore.Save(_settings);

            // Cargar lista inicial de buses.
            ReloadAvailableBuses();

            // Si el bus guardado ya no existe, caer al primary.
            if (string.IsNullOrEmpty(_settings.OutputBusId) ||
                AvailableBuses.All(b => b.Id != _settings.OutputBusId))
            {
                var primaryId = SafePrimaryBusId();
                if (!string.IsNullOrEmpty(primaryId) && _settings.OutputBusId != primaryId)
                {
                    _settings.OutputBusId = primaryId;
                    LocutionSettingsStore.Save(_settings);
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPlay));
        }
    }

    /// <summary>
    /// Recarga la lista de buses disponibles desde el engine.
    /// </summary>
    public void ReloadAvailableBuses()
    {
        try
        {
            AvailableBuses.Clear();

            foreach (var bus in _audioEngine.Buses)
                AvailableBuses.Add(bus);

            OnPropertyChanged(nameof(SelectedBus));
        }
        catch { }
    }

    private string SafePrimaryBusId()
    {
        try { return _audioEngine.PrimaryBus.Id; }
        catch { return "primary"; }
    }

    // ───── Estado ────────────────────────────────────────────────────────

    /// <summary>Ciudad, Pais (ej: "Buenos Aires, Argentina").</summary>
    [ObservableProperty] private string _locationText = "Sin datos";

    /// <summary>Temperatura formateada (ej: "22°C"). "---" si no hay datos.</summary>
    [ObservableProperty] private string _temperatureText = "---";

    /// <summary>Humedad formateada (ej: "65%"). "---" si no hay datos.</summary>
    [ObservableProperty] private string _humidityText = "---";

    /// <summary>True si RMVoice esta disponible e inicializado.</summary>
    [ObservableProperty] private bool _isAvailable;

    /// <summary>True si hay una locución reproduciendose ahora.</summary>
    [ObservableProperty] private bool _isPlaying;

    /// <summary>True si hay datos de clima en cache.</summary>
    [ObservableProperty] private bool _hasWeatherData;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    /// <summary>True si se puede disparar una locución (RMVoice OK y no sonando).</summary>
    public bool CanPlay => IsAvailable && !IsPlaying;

    /// <summary>Texto de ubicacion mostrado en el panel.</summary>
    public string LocationDisplayText =>
        HasWeatherData ? LocationText : "Sin datos";

    /// <summary>Textos de clima mostrados en el panel.</summary>
    public string TemperatureDisplayText => HasWeatherData ? TemperatureText : "---";
    public string HumidityDisplayText => HasWeatherData ? HumidityText : "---";

    /// <summary>Tooltip del boton de clima segun estado.</summary>
    public string ClimateButtonTooltip =>
        !IsAvailable ? "RMVoice no esta disponible"
        : IsPlaying ? "Reproduciendo locucion..."
        : "Reproducir clima";

    /// <summary>Tooltip del boton de hora segun estado.</summary>
    public string HourButtonTooltip =>
        !IsAvailable ? "RMVoice no esta disponible"
        : IsPlaying ? "Reproduciendo locucion..."
        : "Reproducir hora";

    // ───── Eventos (los consume MainWindowViewModel) ─────────────────────

    /// <summary>El usuario pidio abrir la ventana de configuracion.</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>El usuario pidio abrir la app RMVoice.exe.</summary>
    public event EventHandler? OpenRMVoiceRequested;

    // =====================================================================
    // ───── CONSTRUCTOR ───────────────────────────────────────────────────
    // =====================================================================
    public LocutionPanelViewModel(
        IAudioEngine audioEngine,
        LocutionService locution,
        LocutionSettings settings,
        Func<IEnumerable<IAudioChannel>>? getActiveChannels = null)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _locution = locution ?? throw new ArgumentNullException(nameof(locution));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _getActiveChannels = getActiveChannels;

        // Estado inicial.
        IsAvailable = _locution.IsAvailable;

        // Suscribirse a cambios de disponibilidad del engine.
        _locution.StateChanged += OnLocutionStateChanged;

        // Suscribirse a cambios de la config (para rearmar el timer).
        _settings.PropertyChanged += OnSettingsChanged;

        // Intentar leer el ultimo clima cacheado (si RMVoice ya lo tenia).
        RefreshFromCache();

        // Arrancar el timer de refresh.
        StartRefreshTimer();

        // Primer fetch (en background) — no bloquea la UI.
        _ = FetchWeatherSafeAsync();

        // Cargar lista inicial de buses.
        ReloadAvailableBuses();

        // Si el bus guardado ya no existe, caer al primary.
        if (string.IsNullOrEmpty(_settings.OutputBusId) ||
            AvailableBuses.All(b => b.Id != _settings.OutputBusId))
        {
            var primaryId = SafePrimaryBusId();
            if (!string.IsNullOrEmpty(primaryId) && _settings.OutputBusId != primaryId)
            {
                _settings.OutputBusId = primaryId;
                LocutionSettingsStore.Save(_settings);
            }
        }
    }

    // ───── Locuciones desde los botones ─────────────────────────────────

    [RelayCommand]
    private async Task PlayClimateAsync()
    {
        await PlayLocutionSafeAsync(LocutionCommand.Climate);
    }

    [RelayCommand]
    private async Task PlayHourAsync()
    {
        await PlayLocutionSafeAsync(LocutionCommand.HourAndMinute);
    }

    private async Task PlayLocutionSafeAsync(LocutionCommand command)
    {
        if (!CanPlay)
        {
            AppLogger.Warning("LOCUTION-PANEL",
                $"No se puede reproducir {command}: " +
                (IsPlaying ? "ya hay una locucion en curso" : "RMVoice no esta disponible"));
            return;
        }

        // Resolver bus + device.
        var bus = GetSelectedBus();
        int deviceIndex = bus?.ResolvedDeviceIndex ?? ResolveOutputDevice();

        // Volumen configurado para la locucion.
        float volume = (float)Math.Clamp(_settings.ButtonLocutionVolume, 0.0, 1.0);

        IsPlaying = true;

        _playCts?.Cancel();
        _playCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // ── Ducking del audio activo en el mismo bus ────────────────────
        var duckState = ApplyDuckIfEnabled(bus);

        try
        {
            AppLogger.Info("LOCUTION-PANEL",
                $"Reproduciendo {command} en device {deviceIndex} (vol {volume:P0}).");

            bool ok = await _locution.PlayAsync(command, deviceIndex, volume, _playCts.Token);

            if (!ok)
            {
                AppLogger.Warning("LOCUTION-PANEL",
                    $"RMVoice no pudo reproducir {command}.");
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warning("LOCUTION-PANEL",
                $"Locucion {command} cancelada por timeout.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("LOCUTION-PANEL",
                $"Error reproduciendo {command}", ex);
        }
        finally
        {
            // Restaurar el audio.
            RestoreDuck(duckState);

            IsPlaying = false;
            _playCts?.Dispose();
            _playCts = null;
        }
    }

    /// <summary>
    /// Devuelve el bus seleccionado para las locuciones del panel, o el
    /// bus primario si no hay uno configurado o no se encuentra.
    /// </summary>
    private IAudioBus? GetSelectedBus()
    {
        try
        {
            var id = _settings.OutputBusId;
            if (!string.IsNullOrEmpty(id))
            {
                var bus = _audioEngine.GetBus(id);
                if (bus is not null) return bus;
            }

            return _audioEngine.PrimaryBus;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Estado del ducking activo: mapa canal -> ganancia original (dB).
    /// </summary>
    private sealed class DuckState
    {
        public Dictionary<IAudioChannel, double> OriginalGains { get; } = new();
    }

    /// <summary>
    /// Baja el volumen de todos los canales activos del bus indicado al
    /// nivel configurado. Devuelve el estado para poder restaurarlo.
    /// Si el ducking esta deshabilitado, devuelve un estado vacio.
    /// </summary>
    private DuckState ApplyDuckIfEnabled(IAudioBus? bus)
    {
        var state = new DuckState();

        if (!_settings.ButtonDuckEnabled) return state;
        if (bus is null) return state;
        if (_getActiveChannels is null) return state;

        try
        {
            // Convertir el nivel (0..1) a dB. Si el nivel es muy bajo,
            // hacemos un piso de -60 dB (practicamente mute).
            double level = Math.Clamp(_settings.ButtonDuckLevel, 0.001, 1.0);
            double duckDb = 20.0 * Math.Log10(level);
            if (duckDb < -60.0) duckDb = -60.0;

            // Snapshot de todos los canales activos.
            var allChannels = _getActiveChannels()
                .Where(ch => ch is not null)
                .ToList();

            if (allChannels.Count == 0)
            {
                // Nada sonando en toda la app: no hay que duckear.
                return state;
            }

            // Filtrar ESTRICTAMENTE por bus. Solo se duckean los canales
            // que estan reproduciendo en el MISMO bus que la locucion.
            //
            // IMPORTANTE: NO hay fallback. Si el bus no tiene canales
            // activos, no se hace nada. Duckear canales de otros buses
            // seria un bug (afecta audio que no corresponde).
            var targetChannels = allChannels
                .Where(ch => string.Equals(ch.BusId ?? string.Empty,
                                           bus.Id,
                                           StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetChannels.Count == 0)
            {
                // El bus target no tiene canales activos: no duckeamos nada.
                AppLogger.Info("LOCUTION-PANEL",
                    $"Sin canales activos en el bus '{bus.DisplayName}'. " +
                    $"Locucion se reproducira sin ducking.");
                return state;
            }

            foreach (var ch in targetChannels)
            {
                try
                {
                    double original = ch.NormalizationGainDb;
                    state.OriginalGains[ch] = original;

                    ch.SetNormalizationGain(original + duckDb);
                }
                catch { }
            }

            AppLogger.Info("LOCUTION-PANEL",
                $"Ducking aplicado a {state.OriginalGains.Count} canal(es) — " +
                $"bus '{bus.DisplayName}' — {duckDb:+0.0;-0.0;0.0} dB.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("LOCUTION-PANEL",
                $"Error aplicando ducking: {ex.Message}");
        }

        return state;
    }

    /// <summary>
    /// Restaura la ganancia original de cada canal afectado por el ducking.
    /// </summary>
    private void RestoreDuck(DuckState state)
    {
        if (state.OriginalGains.Count == 0) return;

        foreach (var kvp in state.OriginalGains)
        {
            try
            {
                kvp.Key.SetNormalizationGain(kvp.Value);
            }
            catch { }
        }

        AppLogger.Info("LOCUTION-PANEL",
            $"Ducking restaurado en {state.OriginalGains.Count} canal(es).");
    }

    /// <summary>
    /// Resuelve el device index de salida para las locuciones disparadas
    /// desde los botones del panel. Usa el bus configurado en las settings.
    /// </summary>
    private int ResolveOutputDevice()
    {
        try
        {
            var busId = _settings.OutputBusId;
            IAudioBus? bus = null;

            if (!string.IsNullOrEmpty(busId))
                bus = _audioEngine.GetBus(busId);

            bus ??= _audioEngine.PrimaryBus;
            return bus.ResolvedDeviceIndex;
        }
        catch
        {
            try { return _audioEngine.PrimaryBus.ResolvedDeviceIndex; } catch { return 0; }
        }
    }

    // ───── Refresh de clima ─────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshWeatherAsync()
    {
        await FetchWeatherSafeAsync();
    }

    private async Task FetchWeatherSafeAsync()
    {
        if (!IsAvailable) return;

        try
        {
            await _locution.RefreshWeatherAsync();
            RefreshFromCache();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("LOCUTION-PANEL",
                $"Error al refrescar clima: {ex.Message}");
        }
    }

    /// <summary>
    /// Lee el clima cacheado del engine y actualiza las propiedades visibles.
    /// </summary>
    private void RefreshFromCache()
    {
        try
        {
            var loc = _locution.ActiveWeatherLocation;
            var snap = _locution.LastWeather;

            // Ubicacion: "Ciudad, Pais" (sin Admin1).
            if (loc is not null)
            {
                LocationText = BuildLocationText(loc);
            }

            // Clima.
            if (snap is not null)
            {
                TemperatureText = $"{Math.Round(snap.Temperature)}°C";
                HumidityText = $"{Math.Round(snap.Humidity)}%";
                HasWeatherData = true;
            }
            else
            {
                TemperatureText = "---";
                HumidityText = "---";
                HasWeatherData = false;
            }
        }
        catch { }

        // Notificar derivadas.
        OnPropertyChanged(nameof(LocationDisplayText));
        OnPropertyChanged(nameof(TemperatureDisplayText));
        OnPropertyChanged(nameof(HumidityDisplayText));
    }

    /// <summary>
    /// Construye el texto "Ciudad, Pais" a partir del WeatherLocation.
    /// Omite el campo Admin1 (provincia/estado) para que sea compacto.
    /// </summary>
    private static string BuildLocationText(WeatherLocation loc)
    {
        var city = (loc.Name ?? string.Empty).Trim();
        var country = (loc.Country ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(city) && string.IsNullOrEmpty(country))
            return "Sin datos";

        if (string.IsNullOrEmpty(city)) return country;
        if (string.IsNullOrEmpty(country)) return city;

        return $"{city}, {country}";
    }

    // ───── Timer de refresh ─────────────────────────────────────────────

    private void StartRefreshTimer()
    {
        StopRefreshTimer();

        if (!_settings.AutoRefreshEnabled) return;

        int minutes = Math.Max(1, _settings.WeatherRefreshMinutes);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(minutes)
        };
        _refreshTimer.Tick += async (_, _) => await FetchWeatherSafeAsync();
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        try { _refreshTimer?.Stop(); } catch { }
        _refreshTimer = null;
    }

    // ───── Comandos de menu contextual ──────────────────────────────────

    [RelayCommand]
    private void RequestOpenSettings()
    {
        try { OpenSettingsRequested?.Invoke(this, EventArgs.Empty); } catch { }
    }

    [RelayCommand]
    private void RequestOpenRMVoice()
    {
        try { OpenRMVoiceRequested?.Invoke(this, EventArgs.Empty); } catch { }
    }

    // ───── Handlers ─────────────────────────────────────────────────────

    private void OnLocutionStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsAvailable = _locution.IsAvailable;
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(ClimateButtonTooltip));
            OnPropertyChanged(nameof(HourButtonTooltip));

            // Si se acaba de inicializar, hacer un primer fetch.
            if (IsAvailable)
                _ = FetchWeatherSafeAsync();
        });
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocutionSettings.AutoRefreshEnabled) ||
            e.PropertyName == nameof(LocutionSettings.WeatherRefreshMinutes))
        {
            Dispatcher.UIThread.Post(StartRefreshTimer);
        }
    }

    partial void OnIsAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(ClimateButtonTooltip));
        OnPropertyChanged(nameof(HourButtonTooltip));
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(ClimateButtonTooltip));
        OnPropertyChanged(nameof(HourButtonTooltip));
    }

    /// <summary>
    /// Fuerza la recarga de buses si la lista esta vacia. Se llama desde
    /// MainWindowViewModel despues de inicializar el engine.
    /// </summary>
    public void EnsureAvailableBusesLoaded()
    {
        if (AvailableBuses.Count == 0)
            ReloadAvailableBuses();
    }

    // ───── Dispose ──────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _refreshTimer?.Stop(); } catch { }
        _refreshTimer = null;

        try { _playCts?.Cancel(); } catch { }
        try { _playCts?.Dispose(); } catch { }
        _playCts = null;

        try { _locution.StateChanged -= OnLocutionStateChanged; } catch { }
        try { _settings.PropertyChanged -= OnSettingsChanged; } catch { }
    }
}