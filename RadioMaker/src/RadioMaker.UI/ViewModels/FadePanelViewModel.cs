using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del panel de fade automatico.
///
/// Cubre dos comportamientos:
///   1) Auto-fade general: todo audio que se reproduzca aplica fade-in al
///      inicio y fade-out antes del final.
///   2) Crossfade entre carts: cuando la botonera esta en modo crossfade,
///      disparar un cart mientras suena otro hace fade-out del que suena
///      y fade-in del nuevo.
/// -------------
public sealed partial class FadePanelViewModel : ViewModelBase
{
    private readonly FadeSettings _settings;
    private IAudioChannel? _channel;

    // ───── Auto-fade general ─────────────────────────────────────────────

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set
        {
            if (_settings.IsEnabled == value) return;
            _settings.IsEnabled = value;
            FadeSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            ApplyToChannel();
        }
    }

    public double Seconds
    {
        get => _settings.Seconds;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 30.0);
            if (Math.Abs(_settings.Seconds - clamped) < 0.01) return;
            _settings.Seconds = clamped;
            FadeSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondsText));
            OnPropertyChanged(nameof(StatusText));
            ApplyToChannel();
        }
    }

    public string SecondsText => $"{Seconds:F1} s";

    public string StatusText
    {
        get
        {
            if (!IsEnabled) return "Fade automatico desactivado.";
            if (_channel is null) return $"Fade automatico: {SecondsText} (activo al reproducir).";
            return $"Fade automatico: {SecondsText} aplicado al canal actual.";
        }
    }

    // ───── Crossfade entre carts ─────────────────────────────────────────

    /// -------------
    /// Modo crossfade de la botonera. Cuando esta activo, disparar un cart
    /// mientras suena otro hace crossfade. Se persiste en disco.
    /// -------------
    public bool CartCrossfadeEnabled
    {
        get => _settings.CartCrossfadeEnabled;
        set
        {
            if (_settings.CartCrossfadeEnabled == value) return;
            _settings.CartCrossfadeEnabled = value;
            FadeSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(CartCrossfadeStatusText));
        }
    }

    /// -------------
    /// Duracion del crossfade en segundos (0.1 a 5.0). Se persiste en disco.
    /// -------------
    public double CartCrossfadeSeconds
    {
        get => _settings.CartCrossfadeSeconds;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 5.0);
            if (Math.Abs(_settings.CartCrossfadeSeconds - clamped) < 0.01) return;
            _settings.CartCrossfadeSeconds = clamped;
            FadeSettingsStore.Save(_settings);

            OnPropertyChanged();
            OnPropertyChanged(nameof(CartCrossfadeSecondsText));
            OnPropertyChanged(nameof(CartCrossfadeStatusText));
        }
    }

    public string CartCrossfadeSecondsText => $"{CartCrossfadeSeconds:F1} s";

    public string CartCrossfadeStatusText =>
        CartCrossfadeEnabled
            ? $"Crossfade activo: {CartCrossfadeSecondsText} entre carts."
            : "Crossfade desactivado: los carts se superponen.";

    // ───── Constructor ───────────────────────────────────────────────────

    public FadePanelViewModel()
        : this(FadeSettingsStore.Load())
    {
    }

    public FadePanelViewModel(FadeSettings settings)
    {
        _settings = settings;
    }

    // ───── Attach ────────────────────────────────────────────────────────

    public void AttachToChannel(IAudioChannel? channel)
    {
        _channel = channel;
        ApplyToChannel();
        OnPropertyChanged(nameof(StatusText));
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void ApplyToChannel()
    {
        if (_channel is null) return;

        try
        {
            _channel.SetAutoFade(IsEnabled, Seconds);
        }
        catch { }
    }
}