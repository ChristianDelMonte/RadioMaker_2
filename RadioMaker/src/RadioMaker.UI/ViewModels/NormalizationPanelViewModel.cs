using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel de normalizacion.
///
/// El analisis de peak/RMS corre AUTOMATICAMENTE al cargar cada archivo
/// (no hay boton "Analizar").
/// -------------
public sealed partial class NormalizationPanelViewModel : ViewModelBase
{
    private readonly NormalizationSettings _settings;
    private IAudioChannel? _channel;
    /// -------------
    /// Se dispara cuando el usuario cambia el modo, el target,
    /// activa/desactiva la normalizacion.
    /// -------------
    public event EventHandler? SettingsChanged;

    public NormalizationMode[] AvailableModes { get; } =
        { NormalizationMode.Peak, NormalizationMode.Rms };

    // ───── Configuracion ─────────────────────────────────────────────────

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set
        {
            if (_settings.IsEnabled == value) return;
            _settings.IsEnabled = value;
            NormalizationSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyNormalizationToChannel();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NormalizationMode Mode
    {
        get => _settings.Mode;
        set
        {
            if (_settings.Mode == value) return;
            _settings.Mode = value;

            if (value == NormalizationMode.Peak && Math.Abs(_settings.TargetDb - (-16.0)) < 0.01)
                _settings.TargetDb = -1.0;
            else if (value == NormalizationMode.Rms && Math.Abs(_settings.TargetDb - (-1.0)) < 0.01)
                _settings.TargetDb = -16.0;

            NormalizationSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetDb));
            OnPropertyChanged(nameof(TargetDbText));
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(GainText));

            ApplyNormalizationToChannel();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TargetDb
    {
        get => _settings.TargetDb;
        set
        {
            if (Math.Abs(_settings.TargetDb - value) < 0.01) return;
            _settings.TargetDb = value;
            NormalizationSettingsStore.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetDbText));
            OnPropertyChanged(nameof(GainText));
            ApplyNormalizationToChannel();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _statusMessage = "Esperando a que se cargue un archivo...";
    [ObservableProperty] private NormalizationResult _result = NormalizationResult.NotAnalyzed;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string PeakDbText => Result.IsAnalyzed ? $"{Result.PeakDb:F2} dB" : "—";
    public string RmsDbText => Result.IsAnalyzed ? $"{Result.RmsDb:F2} dB" : "—";
    public string CrestFactorText => Result.IsAnalyzed ? $"{Result.CrestFactorDb:F2} dB" : "—";
    public string TargetDbText => $"{TargetDb:F1} dB";
    public string TotalDurationText => Result.IsAnalyzed
        ? $"{Result.TotalDuration:mm\\:ss\\.f}"
        : "—";

    public string ModeDescription => Mode switch
    {
        NormalizationMode.Peak => "Peak: lleva el pico al target, conserva la dinamica.",
        NormalizationMode.Rms  => "RMS: empareja la sonoridad percibida entre archivos.",
        _ => string.Empty
    };

    public double CurrentGainDb => Result.IsAnalyzed
        ? Result.ComputeGainDb(Mode, TargetDb)
        : 0.0;

    public string GainText => Result.IsAnalyzed
        ? $"{CurrentGainDb:+0.00;-0.00;0.00} dB"
        : "—";

    public string TrimStatusText
    {
        get
        {
            if (!IsEnabled) return "Normalizacion desactivada.";
            if (!Result.IsAnalyzed) return "Normalizacion activa - esperando analisis.";
            return $"Normalizacion activa: {GainText}.";
        }
    }

    public bool HasResult => Result.IsAnalyzed;

    // ───── Constructor ───────────────────────────────────────────────────

    public NormalizationPanelViewModel()
        : this(NormalizationSettingsStore.Load())
    {
    }

    public NormalizationPanelViewModel(NormalizationSettings settings)
    {
        _settings = settings;
    }

    // ───── Attach ────────────────────────────────────────────────────────

    public void AttachToChannel(IAudioChannel? channel)
    {
        _channel = channel;

        Result = NormalizationResult.NotAnalyzed;
        StatusMessage = channel is null
            ? "Sin canal cargado."
            : "Analizando niveles...";

        RefreshTexts();

        if (channel is not null)
        {
            _ = AnalyzeAsync();
        }
    }

    // ───── Analisis automatico ───────────────────────────────────────────

    private async Task AnalyzeAsync()
    {
        var channel = _channel;
        if (channel is null) return;
        if (IsAnalyzing) return;

        IsAnalyzing = true;
        OnPropertyChanged(nameof(TrimStatusText));

        try
        {
            var result = await Task.Run(() => channel.AnalyzeLevels());
            Result = result;

            StatusMessage = result.IsAnalyzed
                ? $"Analisis completado. Peak: {result.PeakDb:F2} dB, RMS: {result.RmsDb:F2} dB."
                : "No se pudo analizar el archivo.";

            ApplyNormalizationToChannel();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Result = NormalizationResult.NotAnalyzed;
        }
        finally
        {
            IsAnalyzing = false;
            RefreshTexts();
        }
    }

    // ───── Aplicacion al canal ───────────────────────────────────────────

    private void ApplyNormalizationToChannel()
    {
        if (_channel is null) return;

        try
        {
            if (!IsEnabled || !Result.IsAnalyzed)
            {
                _channel.ClearNormalization();
            }
            else
            {
                double gain = Result.ComputeGainDb(Mode, TargetDb);
                _channel.SetNormalizationGain(gain);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al aplicar normalizacion: {ex.Message}";
        }

        OnPropertyChanged(nameof(TrimStatusText));
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void RefreshTexts()
    {
        OnPropertyChanged(nameof(PeakDbText));
        OnPropertyChanged(nameof(RmsDbText));
        OnPropertyChanged(nameof(CrestFactorText));
        OnPropertyChanged(nameof(TargetDbText));
        OnPropertyChanged(nameof(TotalDurationText));
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(CurrentGainDb));
        OnPropertyChanged(nameof(GainText));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(TrimStatusText));
    }

    partial void OnResultChanged(NormalizationResult value) => RefreshTexts();
    partial void OnIsAnalyzingChanged(bool value) => OnPropertyChanged(nameof(TrimStatusText));
}