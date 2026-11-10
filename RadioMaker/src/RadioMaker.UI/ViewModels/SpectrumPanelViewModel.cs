using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Controls;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del panel Spectrum.
///
/// Mantiene el canal actualmente monitoreado (el canal activo de cualquier
/// modulo: manual, scheduler, playlist o cart) y expone:
///   - El modo de visualizacion actual.
///   - El estado de reproduccion.
///   - Comandos para cambiar el modo y resetear el analizador.
/// </summary>
public sealed partial class SpectrumPanelViewModel : ViewModelBase
{
    private IAudioChannel? _channel;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private SpectrumMode _mode = SpectrumMode.Fft;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private string _statusMessage = "Sin canal activo.";

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public IAudioChannel? Channel => _channel;
    public bool HasChannel => _channel is not null;

    public string SourceText { get; private set; } = string.Empty;

    /// <summary>Lista de modos disponibles para el ComboBox.</summary>
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
    }

    // ───── API publica ───────────────────────────────────────────────────

    /// <summary>
    /// Actualiza el canal monitoreado. Se llama desde MainWindowViewModel
    /// cada vez que cambia el canal activo (o se detiene).
    ///
    /// <paramref name="sourceLabel"/> es una etiqueta para mostrar en el
    /// status: "MANUAL", "CART", "SCHEDULER", "PLAYLIST", etc.
    /// </summary>
    public void SetChannel(IAudioChannel? channel, string sourceLabel = "")
    {
        if (ReferenceEquals(_channel, channel) &&
            string.Equals(SourceText, sourceLabel, StringComparison.Ordinal))
        {
            return;
        }

        _channel = channel;
        SourceText = sourceLabel ?? string.Empty;

        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(HasChannel));
        OnPropertyChanged(nameof(SourceText));

        if (channel is null)
        {
            StatusMessage = "Sin canal activo.";
        }
        else
        {
            string file = string.IsNullOrEmpty(channel.FilePath)
                ? "(sin archivo)"
                : Path.GetFileName(channel.FilePath);

            StatusMessage = string.IsNullOrEmpty(SourceText)
                ? $"Monitoreando: {file}"
                : $"[{SourceText}] {file}";
        }
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    /// <summary>Cicla al siguiente modo (como el click en el control).</summary>
    [RelayCommand]
    private void NextMode()
    {
        int next = ((int)Mode + 1) % AvailableModes.Length;
        Mode = AvailableModes[next];
    }

    /// <summary>Activa o desactiva el analisis.</summary>
    [RelayCommand]
    private void ToggleRunning()
    {
        IsRunning = !IsRunning;
        StatusMessage = IsRunning
            ? "Analisis activo."
            : "Analisis pausado.";
    }
}