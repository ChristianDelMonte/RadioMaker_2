using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Mini osciloscopio (waveform) estilo LED display.
///
/// Version compacta del modo Waveform del SpectrumAnalyzer, pensada para
/// mostrarse dentro del panel "En reproduccion" del top. Dibuja la forma
/// de onda del canal activo con la paleta cyan de los LEDs.
///
/// - Un solo trazo por canal (mono o estereo superpuestos en el centro).
/// - Linea central como referencia.
/// - Fondo oscuro fijo (es un visualizador, no sigue el tema).
/// </summary>
public sealed class MiniWaveform : Control
{
    // ───── Propiedad: canal fuente ───────────────────────────────────────

    public static readonly StyledProperty<IAudioChannel?> ChannelProperty =
        AvaloniaProperty.Register<MiniWaveform, IAudioChannel?>(nameof(Channel));

    public static readonly StyledProperty<IAudioSpectrum?> SpectrumProperty =
        AvaloniaProperty.Register<MiniWaveform, IAudioSpectrum?>(nameof(Spectrum));

    /// <summary>Color del trazo cuando hay audio.</summary>
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<MiniWaveform, IBrush?>(nameof(StrokeBrush));

    /// <summary>Color del trazo cuando NO hay audio (apagado).</summary>
    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<MiniWaveform, IBrush?>(nameof(InactiveBrush));

    public IAudioChannel? Channel
    {
        get => GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    /// <summary>Fuente de espectro opcional para audio sin IAudioChannel (p. ej. RMVoice).</summary>
    public IAudioSpectrum? Spectrum
    {
        get => GetValue(SpectrumProperty);
        set => SetValue(SpectrumProperty, value);
    }

    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public IBrush? InactiveBrush
    {
        get => GetValue(InactiveBrushProperty);
        set => SetValue(InactiveBrushProperty, value);
    }

    // ───── Constantes ────────────────────────────────────────────────────

    private static readonly Color BackgroundColor = Color.Parse("#020508");
    private static readonly Color ActiveColor = Color.Parse("#00E5FF");
    private static readonly Color InactiveColor = Color.Parse("#0D3D4A");
    private static readonly Color CenterLineColor = Color.Parse("#0A2A33");

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(BackgroundColor);
    private static readonly IBrush DefaultActiveBrush = new SolidColorBrush(ActiveColor);
    private static readonly IBrush DefaultInactiveBrush = new SolidColorBrush(InactiveColor);
    private static readonly IBrush CenterLineBrush = new SolidColorBrush(CenterLineColor);

    // ───── Estado interno ────────────────────────────────────────────────

    private float[] _waveBuffer = new float[4096];
    private readonly DispatcherTimer _timer;

    public MiniWaveform()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => InvalidateVisual();

        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChannelProperty ||
            change.Property == SpectrumProperty ||
            change.Property == StrokeBrushProperty ||
            change.Property == InactiveBrushProperty)
        {
            InvalidateVisual();
        }
    }

    // ───── Render ────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 8 || bounds.Height < 8) return;

        // Fondo.
        ctx.FillRectangle(BackgroundBrush, bounds);

        double centerY = bounds.Height / 2.0;

        // Linea central de referencia (eje 0).
        ctx.DrawLine(new Pen(CenterLineBrush, 1),
            new Point(0, centerY),
            new Point(bounds.Width, centerY));

        var spectrum = Spectrum ?? Channel?.Spectrum;
        if (spectrum is null)
        {
            DrawFlatLine(ctx, bounds, centerY, DefaultInactiveBrush);
            return;
        }

        try
        {
            // Cantidad de frames = ancho en pixeles (1 muestra por pixel).
            int frames = (int)bounds.Width;
            if (frames <= 1) return;

            int channels = Math.Max(1, spectrum.ChannelCount);
            int needed = frames * channels;

            if (_waveBuffer.Length < needed)
                _waveBuffer = new float[needed];

            if (!spectrum.ReadWaveform(_waveBuffer, frames))
            {
                DrawFlatLine(ctx, bounds, centerY, DefaultInactiveBrush);
                return;
            }

            var activeBrush = StrokeBrush ?? DefaultActiveBrush;
            var pen = new Pen(activeBrush, 1.4);

            // Dibujar un trazo por canal. Si es estereo, el izquierdo va
            // en la mitad superior y el derecho en la inferior (acotados
            // al 40% del alto cada uno para que no se pisen).
            int traceCount = channels == 1 ? 1 : 2;
            double halfHeight = bounds.Height * 0.40;

            for (int c = 0; c < traceCount; c++)
            {
                double traceCenter = channels == 1
                    ? centerY
                    : (c == 0 ? centerY - bounds.Height * 0.20 : centerY + bounds.Height * 0.20);

                double prevX = 0;
                double prevY = traceCenter;

                for (int x = 0; x < frames; x++)
                {
                    int idx = x * channels + c;
                    if (idx >= _waveBuffer.Length) break;

                    float sample = _waveBuffer[idx];
                    if (sample > 1f) sample = 1f;
                    if (sample < -1f) sample = -1f;

                    double y = traceCenter - sample * halfHeight;

                    if (x > 0)
                        ctx.DrawLine(pen, new Point(prevX, prevY), new Point(x, y));

                    prevX = x;
                    prevY = y;
                }
            }
        }
        catch
        {
            DrawFlatLine(ctx, bounds, centerY, DefaultInactiveBrush);
        }
    }

    private static void DrawFlatLine(DrawingContext ctx, Rect bounds, double y, IBrush brush)
    {
        var pen = new Pen(brush, 1.2);
        ctx.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
    }
}
