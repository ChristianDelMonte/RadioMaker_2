using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RadioMaker.UI.Controls;

/// -------------
/// Barra de progreso vertical que simula una onda de audio.
///
/// Layout:
///   - Se dibuja una pila de N barras horizontales de abajo hacia arriba.
///   - Cada barra tiene una amplitud que oscila en el tiempo (una onda
///     sinusoidal + ruido + energia variable) para simular un waveform.
///   - El progreso (0..1) determina hasta que altura se "encienden" las
///     barras. Abajo = 0%, arriba = 100%.
///   - Las barras encendidas usan ActiveBrush; las apagadas, InactiveBrush.
///
/// La animacion corre con un DispatcherTimer a ~20 fps (50 ms). Se activa
/// solo cuando el control esta en el arbol visual (AttachedToVisualTree).
/// -------------
public sealed class AudioWaveProgressBar : Control
{
    // ───── Propiedades bindables ─────────────────────────────────────────

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, double>(nameof(Progress));

    public static readonly StyledProperty<int> BarCountProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, int>(nameof(BarCount), 48);

    public static readonly StyledProperty<IBrush?> ActiveBrushProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, IBrush?>(nameof(ActiveBrush));

    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, IBrush?>(nameof(InactiveBrush));

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, IBrush?>(nameof(BackgroundBrush));

    public static readonly StyledProperty<double> BarThicknessProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, double>(nameof(BarThickness), 3.0);

    public static readonly StyledProperty<double> MinBarWidthProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, double>(nameof(MinBarWidth), 3.0);

    public static readonly StyledProperty<bool> AnimationEnabledProperty =
        AvaloniaProperty.Register<AudioWaveProgressBar, bool>(nameof(AnimationEnabled), true);

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public int BarCount { get => GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }
    public IBrush? ActiveBrush { get => GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public IBrush? InactiveBrush { get => GetValue(InactiveBrushProperty); set => SetValue(InactiveBrushProperty, value); }
    public IBrush? BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public double BarThickness { get => GetValue(BarThicknessProperty); set => SetValue(BarThicknessProperty, value); }
    public double MinBarWidth { get => GetValue(MinBarWidthProperty); set => SetValue(MinBarWidthProperty, value); }
    public bool AnimationEnabled { get => GetValue(AnimationEnabledProperty); set => SetValue(AnimationEnabledProperty, value); }

    // ───── Estado interno ────────────────────────────────────────────────

    private float[] _amplitudes = Array.Empty<float>();
    private float[] _targets = Array.Empty<float>();

    private DispatcherTimer? _timer;
    private double _phase;
    private readonly Random _rng = new();

    // ───── Constructores y ciclo de vida ─────────────────────────────────

    static AudioWaveProgressBar()
    {
        AffectsRender<AudioWaveProgressBar>(
            ProgressProperty,
            BarCountProperty,
            ActiveBrushProperty,
            InactiveBrushProperty,
            BackgroundBrushProperty,
            BarThicknessProperty,
            MinBarWidthProperty,
            AnimationEnabledProperty);
    }

    public AudioWaveProgressBar()
    {
        AttachedToVisualTree += (_, _) => StartAnimation();
        DetachedFromVisualTree += (_, _) => StopAnimation();
    }

    private void StartAnimation()
    {
        if (_timer != null) return;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        EnsureBuffers();
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
    }

    // ───── Animacion ─────────────────────────────────────────────────────

    private void OnTick()
    {
        if (!AnimationEnabled) return;
        if (_amplitudes.Length == 0) EnsureBuffers();

        _phase += 0.15;

        for (int i = 0; i < _targets.Length; i++)
        {
            double t = (double)i / _targets.Length;

            double baseWave = 0.5 + 0.5 * Math.Sin(_phase + t * 12.0);
            double envelope = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(_phase * 0.7 + t * 3.0));
            double noise = (_rng.NextDouble() - 0.5) * 0.3;

            double value = Math.Clamp(baseWave * envelope + noise, 0.05, 1.0);
            _targets[i] = (float)value;
        }

        const float easing = 0.35f;
        for (int i = 0; i < _amplitudes.Length; i++)
        {
            _amplitudes[i] += (_targets[i] - _amplitudes[i]) * easing;
        }

        InvalidateVisual();
    }

    private void EnsureBuffers()
    {
        int n = Math.Max(4, BarCount);
        if (_amplitudes.Length == n) return;

        _amplitudes = new float[n];
        _targets = new float[n];

        for (int i = 0; i < n; i++)
        {
            _amplitudes[i] = 0.15f;
            _targets[i] = 0.15f;
        }
    }

    // ───── Render ────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 8 || bounds.Height < 8) return;

        EnsureBuffers();

        int n = _amplitudes.Length;
        double thickness = Math.Max(1.0, BarThickness);

        double totalThickness = n * thickness;
        double gap = bounds.Height > totalThickness
            ? (bounds.Height - totalThickness) / Math.Max(1, n - 1)
            : 0.0;

        if (bounds.Height < totalThickness && n > 0)
        {
            thickness = bounds.Height / n;
            gap = 0.0;
        }

        double centerX = bounds.Width / 2;
        double maxHalfWidth = Math.Max(1.0, bounds.Width / 2 - 2);

        double progress = Math.Clamp(Progress, 0.0, 1.0);

        var activeBrush = ActiveBrush ?? new SolidColorBrush(Color.Parse("#00E5FF"));
        var inactiveBrush = InactiveBrush ?? new SolidColorBrush(Color.Parse("#1A2E3A"));
        var bgBrush = BackgroundBrush ?? new SolidColorBrush(Color.Parse("#0A0A0F"));

        ctx.FillRectangle(bgBrush, bounds);

        // Barras de ABAJO hacia ARRIBA.
        for (int i = 0; i < n; i++)
        {
            double y = bounds.Bottom - (i + 0.5) * (thickness + gap);

            double barPosition = n > 1 ? (double)i / (n - 1) : 0.0;
            bool isActive = barPosition <= progress;

            float amp = _amplitudes[i];

            double halfWidth = isActive
                ? maxHalfWidth * (0.30 + 0.70 * amp)
                : Math.Min(maxHalfWidth, MinBarWidth / 2.0);

            halfWidth = Math.Max(0.5, halfWidth);

            double left = centerX - halfWidth;
            double width = halfWidth * 2;

            var rect = new Rect(left, y, width, thickness);
            ctx.FillRectangle(isActive ? activeBrush : inactiveBrush, rect);
        }
    }
}