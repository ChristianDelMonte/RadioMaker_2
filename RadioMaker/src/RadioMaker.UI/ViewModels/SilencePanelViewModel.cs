using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel de analisis de silencios.
///
/// El analisis corre AUTOMATICAMENTE al cargar cada archivo (no hay
/// boton "Analizar"). Los resultados se cachean en el ChannelConfigurator
/// para las reproducciones siguientes.
/// -------------
public sealed partial class SilencePanelViewModel : ViewModelBase
{
    private readonly SilencePanelSettings _settings;
    private IAudioChannel? _channel;
    /// -------------
    /// Se dispara cuando el usuario cambia el umbral, activa/desactiva el
    /// auto-trim o modifica algun parametro de analisis.
    /// -------------
    public event EventHandler? SettingsChanged;

    // ───── Configuracion ─────────────────────────────────────────────────

    public bool AutoTrimEnabled
    {
        get => _settings.AutoTrimEnabled;
        set
        {
            if (_settings.AutoTrimEnabled == value) return;
            _settings.AutoTrimEnabled = value;
            SilencePanelSettingsStore.Save(_settings);
            OnPropertyChanged();
            ApplyTrimToChannel();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ThresholdDb
    {
        get => _settings.ThresholdDb;
        set
        {
            if (Math.Abs(_settings.ThresholdDb - value) < 0.01) return;
            _settings.ThresholdDb = value;
            SilencePanelSettingsStore.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdDbText));

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int BlockMs
    {
        get => _settings.BlockMs;
        set
        {
            if (_settings.BlockMs == value) return;
            _settings.BlockMs = value;
            SilencePanelSettingsStore.Save(_settings);
            OnPropertyChanged();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int MinSilenceMs
    {
        get => _settings.MinSilenceMs;
        set
        {
            if (_settings.MinSilenceMs == value) return;
            _settings.MinSilenceMs = value;
            SilencePanelSettingsStore.Save(_settings);
            OnPropertyChanged();

            // Notificar al exterior.
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.Controls.Silence.Status.Waiting");
    [ObservableProperty] private SilenceAnalysisResult _result = SilenceAnalysisResult.NotAnalyzed;

    // ───── Textos formateados ────────────────────────────────────────────

    public string TotalDurationText => FormatMs(Result.TotalDuration);
    public string StartSilenceText => FormatMs(Result.StartSilence);
    public string EndSilenceText => FormatMs(Result.EndSilence);
    public string EffectiveDurationText => FormatMs(Result.EffectiveDuration);
    public string InternalSilenceTotalText => FormatMs(Result.InternalSilenceTotal);
    public string ThresholdDbText => $"{ThresholdDb:F0} dB";

    public string InternalSilenceCountText =>
        Result.InternalSilenceCount == 0
            ? LocalizationService.GetString("Loc.Controls.Silence.None")
            : $"{Result.InternalSilenceCount}";

    public bool HasResult => Result.IsAnalyzed;
    public bool HasChannel => _channel is not null;
    public bool HasSignificantTrim => Result.HasSignificantTrim;
    public bool NoSignificantTrim => !Result.HasSignificantTrim;

    public string TrimStatusText
    {
        get
        {
            if (!AutoTrimEnabled) return LocalizationService.GetString("Loc.Controls.Silence.Trim.Disabled");
            if (!Result.IsAnalyzed) return LocalizationService.GetString("Loc.Controls.Silence.Trim.Waiting");
            if (!Result.HasSignificantTrim) return LocalizationService.GetString("Loc.Controls.Silence.Trim.NoTrim");
            return string.Format(LocalizationService.GetString("Loc.Controls.Silence.Trim.Applied"), FormatMs(Result.StartSilence), FormatMs(Result.EndSilence));
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public SilencePanelViewModel()
        : this(SilencePanelSettingsStore.Load())
    {
    }

    public SilencePanelViewModel(SilencePanelSettings settings)
    {
        _settings = settings;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    // ───── Attach ────────────────────────────────────────────────────────

    /// -------------
    /// Vincula el panel a un canal. SIEMPRE dispara el analisis automatico
    /// (ya no hay boton manual).
    /// -------------
    public void AttachToChannel(IAudioChannel? channel)
    {
        _channel = channel;

        Result = SilenceAnalysisResult.NotAnalyzed;
        StatusMessage = channel is null
            ? LocalizationService.GetString("Loc.Controls.Silence.Status.NoChannel")
            : LocalizationService.GetString("Loc.Controls.Silence.Status.Analyzing");

        OnPropertyChanged(nameof(HasChannel));
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
            double threshold = ThresholdDb;
            int block = BlockMs;
            int minSilence = MinSilenceMs;

            var result = await Task.Run(() =>
                channel.AnalyzeSilence(threshold, block, minSilence));

            Result = result;

            StatusMessage = result.IsAnalyzed
                ? string.Format(LocalizationService.GetString("Loc.Controls.Silence.Status.Completed"), FormatMs(result.StartSilence), FormatMs(result.EndSilence))
                : LocalizationService.GetString("Loc.Controls.Silence.Status.Failed");

            ApplyTrimToChannel();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.Controls.Silence.Status.Error"), ex.Message);
            Result = SilenceAnalysisResult.NotAnalyzed;
        }
        finally
        {
            IsAnalyzing = false;
            RefreshTexts();
        }
    }

    // ───── Aplicacion del trim ───────────────────────────────────────────

    private void ApplyTrimToChannel()
    {
        if (_channel is null) return;

        try
        {
            if (!AutoTrimEnabled || !Result.IsAnalyzed)
            {
                _channel.ClearTrim();
            }
            else
            {
                _channel.SetTrim(Result.StartSilence, Result.EndSilence);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.Controls.Silence.Status.TrimError"), ex.Message);
        }

        OnPropertyChanged(nameof(TrimStatusText));
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private static string FormatMs(TimeSpan ts)
    {
        if (ts.TotalMilliseconds < 1) return "0 ms";
        if (ts.TotalSeconds < 1) return $"{ts.TotalMilliseconds:F0} ms";
        return $"{ts:mm\\:ss\\.f}";
    }

    private void RefreshTexts()
    {
        OnPropertyChanged(nameof(TotalDurationText));
        OnPropertyChanged(nameof(StartSilenceText));
        OnPropertyChanged(nameof(EndSilenceText));
        OnPropertyChanged(nameof(EffectiveDurationText));
        OnPropertyChanged(nameof(InternalSilenceTotalText));
        OnPropertyChanged(nameof(InternalSilenceCountText));
        OnPropertyChanged(nameof(ThresholdDbText));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasSignificantTrim));
        OnPropertyChanged(nameof(NoSignificantTrim));
        OnPropertyChanged(nameof(TrimStatusText));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        StatusMessage = IsAnalyzing
            ? LocalizationService.GetString("Loc.Controls.Silence.Status.Analyzing")
            : _channel is null
                ? LocalizationService.GetString("Loc.Controls.Silence.Status.NoChannel")
                : Result.IsAnalyzed
                    ? string.Format(LocalizationService.GetString("Loc.Controls.Silence.Status.Completed"), FormatMs(Result.StartSilence), FormatMs(Result.EndSilence))
                    : LocalizationService.GetString("Loc.Controls.Silence.Status.Waiting");
        RefreshTexts();
    }

    partial void OnResultChanged(SilenceAnalysisResult value) => RefreshTexts();
    partial void OnIsAnalyzingChanged(bool value) => OnPropertyChanged(nameof(HasChannel));
}
