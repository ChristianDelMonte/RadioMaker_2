using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel de ducking automatico.
///
/// Responsabilidades:
///   - Exponer la configuracion al usuario (device, umbral, reduccion, etc).
///   - Aplicarla al engine cuando cambia algo.
///   - Refrescar periodicamente los niveles actuales (microfono y ganancia aplicada).
/// -------------
public sealed partial class DuckingPanelViewModel : ViewModelBase
{
    private readonly DuckingSettings _settings;
    private readonly IAudioEngine? _engine;
    private readonly DispatcherTimer _refreshTimer;

    // ───── Configuracion ─────────────────────────────────────────────────

    public ObservableCollection<MicDeviceInfo> AvailableMics { get; } = new();

    [ObservableProperty] private MicDeviceInfo? _selectedMic;

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set
        {
            if (_settings.IsEnabled == value) return;
            _settings.IsEnabled = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyToEngine();
        }
    }

    public double ThresholdDb
    {
        get => _settings.ThresholdDb;
        set
        {
            if (Math.Abs(_settings.ThresholdDb - value) < 0.1) return;
            _settings.ThresholdDb = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdDbText));
            ApplyToEngine();
        }
    }

    public double ReductionDb
    {
        get => _settings.ReductionDb;
        set
        {
            if (Math.Abs(_settings.ReductionDb - value) < 0.1) return;
            _settings.ReductionDb = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReductionDbText));
            ApplyToEngine();
        }
    }

    public int HoldMs
    {
        get => _settings.HoldMs;
        set
        {
            if (_settings.HoldMs == value) return;
            _settings.HoldMs = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyToEngine();
        }
    }

    public int AttackMs
    {
        get => _settings.AttackMs;
        set
        {
            if (_settings.AttackMs == value) return;
            _settings.AttackMs = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyToEngine();
        }
    }

    public int ReleaseMs
    {
        get => _settings.ReleaseMs;
        set
        {
            if (_settings.ReleaseMs == value) return;
            _settings.ReleaseMs = value;
            DuckingSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyToEngine();
        }
    }

    // ───── Estado en vivo ────────────────────────────────────────────────

    [ObservableProperty] private double _currentMicLevelDb = -120.0;
    [ObservableProperty] private double _currentGainDb = 0.0;
    [ObservableProperty] private string _statusMessage = "Ducking desactivado.";

    public string ThresholdDbText => $"{ThresholdDb:F0} dB";
    public string ReductionDbText => $"-{ReductionDb:F0} dB";
    public string MicLevelText => CurrentMicLevelDb <= -119
        ? "-inf dB"
        : $"{CurrentMicLevelDb:F1} dB";
    public string CurrentGainText => $"{CurrentGainDb:F1} dB";

    /// -------------Indica si el micrófono esta captando voz por encima del umbral.</summary>
    public bool IsVoiceDetected => CurrentMicLevelDb > ThresholdDb;

    /// -------------Indica si la atenuacion esta activa (ganancia < -0.5 dB).</summary>
    public bool IsDuckingActive => CurrentGainDb < -0.5;

    // ───── Constructor ───────────────────────────────────────────────────

    public DuckingPanelViewModel()
        : this(DuckingSettingsStore.Load(), null)
    {
    }

    public DuckingPanelViewModel(DuckingSettings settings, IAudioEngine? engine)
    {
        _settings = settings;
        _engine = engine;

        LoadMics();

        // Si el engine reporta que el ducking esta activo, sincronizar el flag.
        if (engine is not null)
        {
            // Suscribirse a los cambios de estado del engine.
            engine.DuckingStateChanged += (_, _) =>
            {
                // Refrescar inmediatamente.
                Refresh();
            };
        }

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += (_, _) => Refresh();

        // NO arrancamos el timer aca. El DuckingPanel lo arranca cuando entra
        // al arbol visual y lo para cuando sale. Asi no desperdiciamos CPU
        // cuando el usuario no esta mirando la seccion de Ducking.
        // _refreshTimer.Start();

        // Aplicar la config al arrancar (por si el usuario la activo la sesion pasada).
        ApplyToEngine();
    }

    public void StartTimer()
    {
        if (_refreshTimer.IsEnabled) return;
        _refreshTimer.Start();
    }

    public void StopTimer() => _refreshTimer.Stop();

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshMics()
    {
        LoadMics();
        StatusMessage = $"Lista de microfonos actualizada ({AvailableMics.Count - 1} dispositivo(s)).";
    }

    private void LoadMics()
    {
        AvailableMics.Clear();

        if (_engine is null)
            return;

        try
        {
            foreach (var mic in _engine.EnumerateMicrophones())
                AvailableMics.Add(mic);

            SelectedMic = AvailableMics.FirstOrDefault(m => m.Index == _settings.MicDeviceIndex)
                          ?? AvailableMics.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error enumerando microfonos: {ex.Message}";
        }
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnSelectedMicChanged(MicDeviceInfo? value)
    {
        if (value is null) return;
        if (value.Index == _settings.MicDeviceIndex) return;

        _settings.MicDeviceIndex = value.Index;
        DuckingSettingsStore.Save(_settings);
        ApplyToEngine();
    }

    // ───── Aplicar al engine ─────────────────────────────────────────────

    private void ApplyToEngine()
    {
        if (_engine is null) return;

        try
        {
            var config = _settings.ToConfig();

            if (config.IsEnabled)
            {
                _engine.ApplyDuckingConfig(config);
                StatusMessage = config.ReductionDb >= 0
                    ? $"Ducking activo: -{config.ReductionDb:F0} dB con umbral {config.ThresholdDb:F0} dB."
                    : "Ducking activo.";
            }
            else
            {
                _engine.StopDucking();
                StatusMessage = "Ducking desactivado.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error aplicando ducking: {ex.Message}";
        }

        Refresh();
    }

    // ───── Refresco periodico ────────────────────────────────────────────

    private void Refresh()
    {
        if (_engine is null) return;

        try
        {
            CurrentMicLevelDb = _engine.CurrentMicLevelDb;
            CurrentGainDb = _engine.CurrentDuckingGainDb;
        }
        catch { }

        OnPropertyChanged(nameof(MicLevelText));
        OnPropertyChanged(nameof(CurrentGainText));
        OnPropertyChanged(nameof(IsVoiceDetected));
        OnPropertyChanged(nameof(IsDuckingActive));
    }
}