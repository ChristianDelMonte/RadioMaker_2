using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Controls;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel Spectrum.
///
/// Mantiene el canal actualmente monitoreado (el canal activo de cualquier
/// modulo: manual, scheduler, playlist o cart) y expone:
///   - El modo de visualizacion actual.
///   - El estado de reproduccion.
///   - Comandos para cambiar el modo y resetear el analizador.
/// -------------
public sealed partial class SpectrumPanelViewModel : ViewModelBase
{
    private IAudioChannel? _channel;
    private IAudioSpectrum? _spectrumSource;
    private string _sourceDisplayName = string.Empty;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private SpectrumMode _mode = SpectrumMode.Fft;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.Controls.Spectrum.Status.NoChannel");

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public IAudioChannel? Channel => _channel;
    public IAudioSpectrum? SpectrumSource => _spectrumSource ?? _channel?.Spectrum;
    public bool HasChannel => SpectrumSource is not null;

    public string SourceText { get; private set; } = string.Empty;

    /// -------------Lista de modos disponibles para el ComboBox.</summary>
    public SpectrumMode[] AvailableModes { get; } =
    {
        SpectrumMode.Fft,
        SpectrumMode.LogBands,
        SpectrumMode.Waterfall,
        SpectrumMode.Waveform
    };

    // ───── Constructor ───────────────────────────────────────────────────

    public SpectrumPanelViewModel()
    {
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    // ───── API publica ───────────────────────────────────────────────────

    /// -------------
    /// Actualiza el canal monitoreado. Se llama desde MainWindowViewModel
    /// cada vez que cambia el canal activo (o se detiene).
    ///
    /// <paramref name="sourceLabel"/> es una etiqueta para mostrar en el
    /// status: "MANUAL", "CART", "SCHEDULER", "PLAYLIST", etc.
    /// -------------
    public void SetChannel(IAudioChannel? channel, string sourceLabel = "")
    {
        if (ReferenceEquals(_channel, channel) && _spectrumSource is null &&
            string.Equals(SourceText, sourceLabel, StringComparison.Ordinal))
        {
            return;
        }

        _channel = channel;
        _spectrumSource = null;
        _sourceDisplayName = string.Empty;
        SourceText = sourceLabel ?? string.Empty;

        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(SpectrumSource));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(SourceText));

        if (channel is null)
        {
            StatusMessage = LocalizationService.GetString("Loc.Controls.Spectrum.Status.NoChannel");
        }
        else
        {
            string file = string.IsNullOrEmpty(channel.FilePath)
                ? "(sin archivo)"
                : Path.GetFileName(channel.FilePath);

            StatusMessage = string.IsNullOrEmpty(SourceText)
                ? string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Monitoring"), file)
                : string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Source"), SourceText, file);
        }
    }

    public void SetSpectrumSource(IAudioSpectrum? spectrum, string sourceLabel, string displayName)
    {
        if (_channel is null && ReferenceEquals(_spectrumSource, spectrum) &&
            string.Equals(SourceText, sourceLabel, StringComparison.Ordinal))
            return;

        _channel = null;
        _spectrumSource = spectrum;
        _sourceDisplayName = displayName ?? string.Empty;
        SourceText = sourceLabel ?? string.Empty;
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(SpectrumSource));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(SourceText));
        StatusMessage = spectrum is null
            ? LocalizationService.GetString("Loc.Controls.Spectrum.Status.NoChannel")
            : string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Source"), SourceText, displayName);
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    /// -------------Cicla al siguiente modo (como el click en el control).</summary>
    [RelayCommand]
    private void NextMode()
    {
        int next = ((int)Mode + 1) % AvailableModes.Length;
        Mode = AvailableModes[next];
    }

    /// -------------Activa o desactiva el analisis.</summary>
    [RelayCommand]
    private void ToggleRunning()
    {
        IsRunning = !IsRunning;
        StatusMessage = LocalizationService.GetString(IsRunning
            ? "Loc.Controls.Spectrum.Status.Active"
            : "Loc.Controls.Spectrum.Status.Paused");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_channel is not null)
        {
            string file = string.IsNullOrEmpty(_channel.FilePath) ? "(sin archivo)" : Path.GetFileName(_channel.FilePath);
            StatusMessage = string.IsNullOrEmpty(SourceText)
                ? string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Monitoring"), file)
                : string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Source"), SourceText, file);
        }
        else if (_spectrumSource is not null)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.Controls.Spectrum.Status.Source"), SourceText, _sourceDisplayName);
        }
        else
        {
            StatusMessage = LocalizationService.GetString(IsRunning
                ? "Loc.Controls.Spectrum.Status.NoChannel"
                : "Loc.Controls.Spectrum.Status.Paused");
        }
    }
}
