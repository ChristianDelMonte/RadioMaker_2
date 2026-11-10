using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Controls;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del panel de configuracion del VU meter.
///
/// El timer interno genera la señal de demo del preview en vivo. NO se
/// arranca automaticamente en el constructor: el <see cref="Controls.VuMeterPanel"/>
/// llama a <see cref="StartTimer"/> cuando el panel entra al arbol visual
/// y a <see cref="StopTimer"/> cuando sale. Esto evita consumir CPU
/// mientras el usuario esta en otra seccion de la ventana de configuracion.
/// </summary>
public sealed partial class VuMeterSettingsWindowViewModel : ViewModelBase
{
    private readonly DispatcherTimer _demoTimer;
    private double _demoPhase;

    public VuMeterSettings Settings { get; }

    [ObservableProperty] private double _previewLeftLevel;
    [ObservableProperty] private double _previewRightLevel;
    [ObservableProperty] private double _previewLeftPeak;
    [ObservableProperty] private double _previewRightPeak;
    [ObservableProperty] private double _previewLeftRms;
    [ObservableProperty] private double _previewRightRms;
    [ObservableProperty] private bool _previewClip;
    [ObservableProperty] private bool _demoEnabled = true;

    public VuMeterStyle[] AvailableStyles { get; } =
        { VuMeterStyle.Console, VuMeterStyle.LedSegments, VuMeterStyle.Minimal };

    public VuMeterOrientation[] AvailableOrientations { get; } =
        { VuMeterOrientation.Vertical, VuMeterOrientation.Horizontal };

    public VuMeterSettingsWindowViewModel(VuMeterSettings settings)
    {
        Settings = settings;
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _demoTimer.Tick += (_, _) => OnDemoTick();

        // NOTA: no arrancamos el timer aca. El VuMeterPanel llama a
        // StartTimer() al entrar al arbol visual.
    }

    /// <summary>
    /// Arranca el timer del preview. Es idempotente.
    /// Lo llama el VuMeterPanel al entrar al arbol visual.
    /// </summary>
    public void StartTimer()
    {
        if (_demoTimer.IsEnabled) return;
        _demoTimer.Start();
    }

    /// <summary>
    /// Detiene el timer del preview. Es idempotente.
    /// Lo llama el VuMeterPanel al salir del arbol visual (cambio de
    /// seccion o cierre de la ventana).
    /// </summary>
    public void StopTimer()
    {
        _demoTimer.Stop();
    }

    private void OnDemoTick()
    {
        if (!DemoEnabled)
        {
            PreviewLeftLevel *= 0.9;
            PreviewRightLevel *= 0.9;
            return;
        }

        _demoPhase += 0.05;
        double kick = Math.Pow(Math.Max(0, Math.Sin(_demoPhase * 4.5)), 3) * 0.85;
        double body = 0.45 + 0.35 * Math.Sin(_demoPhase * 1.3) * Math.Sin(_demoPhase * 0.7);
        double signal = kick * 0.6 + body * 0.7;

        double l = Math.Clamp(signal * (0.95 + 0.05 * Math.Sin(_demoPhase * 1.1)), 0, 1);
        double r = Math.Clamp(signal * (0.95 + 0.05 * Math.Cos(_demoPhase * 1.3)), 0, 1);

        PreviewLeftLevel = AttackDecay(PreviewLeftLevel, l);
        PreviewRightLevel = AttackDecay(PreviewRightLevel, r);
        PreviewLeftPeak = Math.Max(PreviewLeftPeak * 0.985, PreviewLeftLevel);
        PreviewRightPeak = Math.Max(PreviewRightPeak * 0.985, PreviewRightLevel);
        PreviewLeftRms = AttackDecay(PreviewLeftRms, PreviewLeftLevel * 0.85);
        PreviewRightRms = AttackDecay(PreviewRightRms, PreviewRightLevel * 0.85);
        PreviewClip = PreviewLeftLevel > 0.97 || PreviewRightLevel > 0.97;
    }

    private static double AttackDecay(double current, double target)
    {
        const double attack = 0.55;
        const double decay = 0.12;
        return target > current
            ? current + (target - current) * attack
            : current + (target - current) * decay;
    }

    [RelayCommand]
    private void ResetColors()
    {
        Settings.GreenZoneColor = "#10B981";
        Settings.AmberZoneColor = "#F59E0B";
        Settings.RedZoneColor = "#EF4444";
        Settings.BackgroundColor = "#1A1A1A";
        Settings.ScaleTextColor = "#9CA3AF";
        Settings.AccentColor = "#4D6BFE";
    }

    [RelayCommand]
    private void ResetLayout()
    {
        Settings.Style = VuMeterStyle.LedSegments;
        Settings.Orientation = VuMeterOrientation.Vertical;
        Settings.ShowDbScale = true;
        Settings.SegmentCount = 24;
        Settings.ShowPeakHold = true;
        Settings.ShowRms = false;
        Settings.ShowClipIndicator = true;
        Settings.BarWidth = 26.0;
        Settings.PeakHoldDecayMs = 1500;
    }
}
