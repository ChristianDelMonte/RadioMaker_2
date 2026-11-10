using System.Collections.ObjectModel;
using RMVoice.UI.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVoice.Core;
using RMVoice.Core.Models;
using RMVoice.Core.Services;

namespace RMVoice.UI.ViewModels;

/// <summary>
/// ViewModel de la pestaña "Clima (Open-Meteo)".
/// Usa <see cref="IAnnouncer"/> como backend (búsqueda, fetch, ubicación activa)
/// y persiste las preferencias de UI (auto-refresh, etc.) en settings.json.
/// </summary>
public partial class WeatherPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAnnouncer _announcer;
    private readonly AppSettingsStore _settingsStore;
    private readonly DispatcherTimer _refreshTimer;

    private CancellationTokenSource? _cts;
    private bool _suppressPersist;
    private bool _disposed;

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private WeatherLocation? _candidate;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private string _statusMessage = "Escribe una ciudad y pulsa Buscar.";
    [ObservableProperty] private string _lastUpdateText = "Sin datos todavía.";
    [ObservableProperty] private bool _autoRefresh;
    [ObservableProperty] private decimal _refreshMinutes = 15m;
    [ObservableProperty] private bool _announceAfterFetch;

    public ObservableCollection<WeatherLocation> Results { get; } = new();

    public WeatherLocation? ActiveLocation => _announcer.ActiveWeatherLocation;

    public string ActiveLocationDisplay => ActiveLocation?.DisplayName ?? "(ninguna seleccionada)";
    public string ActiveLocationCoordinates => ActiveLocation?.CoordinatesText ?? "";

    /// <summary>Se dispara cuando se obtiene un snapshot con éxito.</summary>
    public event Action<WeatherSnapshot>? WeatherUpdated;

    /// <summary>Se dispara cuando falla una consulta.</summary>
    public event Action<string>? FetchFailed;

    public WeatherPanelViewModel(IAnnouncer announcer)
    {
        _announcer = announcer;
        _settingsStore = new AppSettingsStore();

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(15),
            DispatcherPriority.Background,
            OnRefreshTimerTick);

        LoadSettings();
        ConfigureTimer();
    }

    // ------------------------------------------------------------------ //
    //  Búsqueda de ubicaciones
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusMessage = "Escribe una ciudad para buscar.";
            return;
        }

        Results.Clear();
        Candidate = null;
        IsSearching = true;
        StatusMessage = $"Buscando «{SearchQuery}»…";

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var found = await _announcer.SearchLocationsAsync(SearchQuery, _cts.Token);

            foreach (var loc in found)
                Results.Add(loc);

            StatusMessage = found.Count == 0
                ? "No se encontraron resultados para esa búsqueda."
                : $"{found.Count} resultado(s). Selecciona uno y pulsa «Usar esta ubicación».";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Búsqueda cancelada por timeout.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Error en la búsqueda: " + ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ------------------------------------------------------------------ //
    //  Aplicar / limpiar ubicación
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private async Task ApplyLocationAsync()
    {
        if (Candidate is null)
        {
            StatusMessage = "Selecciona primero un resultado de la lista.";
            return;
        }

        _announcer.ActiveWeatherLocation = Candidate;
        PersistSettings();

        OnPropertyChanged(nameof(ActiveLocation));
        OnPropertyChanged(nameof(ActiveLocationDisplay));
        OnPropertyChanged(nameof(ActiveLocationCoordinates));

        StatusMessage = $"Ubicación aplicada: {Candidate.DisplayName}";
        await FetchNowAsync();
    }

    [RelayCommand]
    private void ClearLocation()
    {
        _announcer.ActiveWeatherLocation = null;
        PersistSettings();

        Candidate = null;
        Results.Clear();
        LastUpdateText = "Sin datos todavía.";
        StatusMessage = "Ubicación y datos limpiados.";

        OnPropertyChanged(nameof(ActiveLocation));
        OnPropertyChanged(nameof(ActiveLocationDisplay));
        OnPropertyChanged(nameof(ActiveLocationCoordinates));
    }

    // ------------------------------------------------------------------ //
    //  Consulta del clima actual
    // ------------------------------------------------------------------ //

    [RelayCommand]
    public async Task FetchNowAsync()
    {
        if (_announcer.ActiveWeatherLocation is null)
        {
            StatusMessage = "No hay ubicación activa. Busca y aplica una primero.";
            return;
        }

        IsFetching = true;
        StatusMessage = $"Obteniendo clima de {_announcer.ActiveWeatherLocation.Name}…";

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var snap = await _announcer.FetchWeatherAsync(_cts.Token);

            LastUpdateText =
                $"Última actualización: {snap.Timestamp:HH:mm:ss} · " +
                $"{snap.Temperature:0.#} °C · {snap.Humidity:0} %";

            StatusMessage = $"Datos obtenidos para {_announcer.ActiveWeatherLocation.DisplayName}.";

            WeatherUpdated?.Invoke(snap);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Timeout al obtener el clima.";
            FetchFailed?.Invoke("timeout");
        }
        catch (Exception ex)
        {
            StatusMessage = "Error: " + ex.Message;
            FetchFailed?.Invoke(ex.Message);
        }
        finally
        {
            IsFetching = false;
        }
    }

    // ------------------------------------------------------------------ //
    //  Refresco automático
    // ------------------------------------------------------------------ //

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await FetchNowAsync();
    }

    private void ConfigureTimer()
    {
        _refreshTimer.Stop();

        if (!AutoRefresh || _announcer.ActiveWeatherLocation is null)
            return;

        var minutes = Math.Max(1, (double)RefreshMinutes);
        _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        _refreshTimer.Start();
    }

    // ------------------------------------------------------------------ //
    //  Persistencia de ajustes
    // ------------------------------------------------------------------ //

    private void LoadSettings()
    {
         _suppressPersist = true;

        var s = _settingsStore.Load();
        AutoRefresh = s.AutoWeatherRefresh;
        RefreshMinutes = s.WeatherRefreshMinutes <= 0 ? 15m : s.WeatherRefreshMinutes;
        AnnounceAfterFetch = s.AnnounceAfterWeatherFetch;

        // La ubicación ya la cargó el Announcer al Initialize. Solo refrescamos la UI.
        OnPropertyChanged(nameof(ActiveLocation));
        OnPropertyChanged(nameof(ActiveLocationDisplay));
        OnPropertyChanged(nameof(ActiveLocationCoordinates));
        OnPropertyChanged(nameof(AutoRefresh));
        OnPropertyChanged(nameof(RefreshMinutes));
        OnPropertyChanged(nameof(AnnounceAfterFetch));

        // Si el engine tiene un snapshot persistido (cargado al Initialize),
        // mostrar el timestamp del último update conocido. Asi el usuario
        // ve "Última actualización: 15:42 · 22.5 °C · 68 %" al abrir, sin
        // necesidad de volver a consultar Open-Meteo.
        if (_announcer.LastWeather is { } lastSnap)
        {
            LastUpdateText =
                $"Última actualización: {lastSnap.Timestamp:HH:mm:ss} · " +
                $"{lastSnap.Temperature:0.#} °C · {lastSnap.Humidity:0} %";
        }

        _suppressPersist = false;
    }

    private void PersistSettings()
    {
        if (_suppressPersist) return;

        _settingsStore.Save(new AppSettings
        {
            ActiveWeatherLocation = _announcer.ActiveWeatherLocation,
            AutoWeatherRefresh = AutoRefresh,
            WeatherRefreshMinutes = (int)RefreshMinutes,
            AnnounceAfterWeatherFetch = AnnounceAfterFetch
        });
    }

    partial void OnAutoRefreshChanged(bool value)
    {
        PersistSettings();
        ConfigureTimer();
    }

    partial void OnRefreshMinutesChanged(decimal value)
    {
        PersistSettings();
        ConfigureTimer();
    }

    partial void OnAnnounceAfterFetchChanged(bool value) => PersistSettings();

    // ------------------------------------------------------------------ //

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}