using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace RadioMaker.UI.Controls;

/// -------------
/// Dial rotativo (knob) para controlar un valor continuo, al estilo de
/// los mandos fisicos de equipos de audio profesionales.
///
/// Caracteristicas:
///   - Rango de giro: -135° a +135° (270° total).
///   - Interaccion: click+arrastre relativo (como agarrar la perilla real),
///     y rueda del mouse para ajustes finos.
///   - Rendering: cuerpo con gradiente radial metalico, aguja indicadora,
///     arco de progreso, marcas de gradacion opcionales.
///   - Se adapta al tamaño: usa min(Width, Height) como diametro.
///   - Los ticks se ocultan automaticamente si el control mide menos de
///     36px, para evitar ruido visual en espacios chicos.
/// -------------
public sealed class RotaryDial : Control
{
    // Angulo de inicio/fin en grados respecto a la vertical (top = 0).
    private const double MinAngle = -135.0;
    private const double MaxAngle = 135.0;
    private const double SweepAngle = MaxAngle - MinAngle;

    // ───── Propiedades bindables ─────────────────────────────────────────

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RotaryDial, double>(
            nameof(Value),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RotaryDial, double>(nameof(Minimum), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RotaryDial, double>(nameof(Maximum), 1.0);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<RotaryDial, double>(nameof(Step), 0.01);

    public static readonly StyledProperty<bool> ShowTicksProperty =
        AvaloniaProperty.Register<RotaryDial, bool>(nameof(ShowTicks), true);

    public static readonly StyledProperty<bool> ShowValueArcProperty =
        AvaloniaProperty.Register<RotaryDial, bool>(nameof(ShowValueArc), true);

    public static readonly StyledProperty<IBrush?> DialFaceBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(DialFaceBrush));

    public static readonly StyledProperty<IBrush?> DialRimBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(DialRimBrush));

    public static readonly StyledProperty<IBrush?> PointerBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(PointerBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> ArcBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(ArcBrush));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<RotaryDial, IBrush?>(nameof(TickBrush));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public bool ShowTicks { get => GetValue(ShowTicksProperty); set => SetValue(ShowTicksProperty, value); }
    public bool ShowValueArc { get => GetValue(ShowValueArcProperty); set => SetValue(ShowValueArcProperty, value); }
    public IBrush? DialFaceBrush { get => GetValue(DialFaceBrushProperty); set => SetValue(DialFaceBrushProperty, value); }
    public IBrush? DialRimBrush { get => GetValue(DialRimBrushProperty); set => SetValue(DialRimBrushProperty, value); }
    public IBrush? PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    // ───── Estado interno ────────────────────────────────────────────────

    private bool _isDragging;
    private double _pressAngle;
    private double _pressValue;

    static RotaryDial()
    {
        AffectsRender<RotaryDial>(
            ValueProperty, MinimumProperty, MaximumProperty,
            ShowTicksProperty, ShowValueArcProperty,
            DialFaceBrushProperty, DialRimBrushProperty, PointerBrushProperty,
            TrackBrushProperty, ArcBrushProperty, TickBrushProperty);
    }

    public RotaryDial()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
    }

    // ───── Render ────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(Bounds.Size);
        double size = Math.Min(bounds.Width, bounds.Height);
        if (size < 12) return;

        double cx = bounds.Center.X;
        double cy = bounds.Center.Y;

        // Radios proporcionales al tamaño.
        double outerR = size / 2 - 1;
        double arcR = outerR - Math.Max(2.0, size * 0.05);
        double knobR = outerR * 0.62;

        // Paleta por defecto (metallic oscuro).
        var faceBrush = DialFaceBrush ?? CreateKnobFaceBrush();
        var rimBrush = DialRimBrush ?? new SolidColorBrush(Color.Parse("#3A4050"));
        var pointerBrush = PointerBrush ?? new SolidColorBrush(Color.Parse("#00E5FF"));
        var trackBrush = TrackBrush ?? new SolidColorBrush(Color.Parse("#2A3140"));
        var arcBrush = ArcBrush ?? new SolidColorBrush(Color.Parse("#00E5FF"));
        var tickBrush = TickBrush ?? new SolidColorBrush(Color.Parse("#5A6274"));

        double thickness = Math.Max(2.0, size * 0.055);

        // ── 1) Arco de fondo (track) ─────────────────────────────────────
        DrawArc(ctx, cx, cy, arcR, MinAngle, MaxAngle, trackBrush, thickness);

        // ── 2) Arco de valor (fill) ──────────────────────────────────────
        double normalized = GetNormalizedValue();
        double valueAngle = MinAngle + normalized * SweepAngle;

        if (ShowValueArc && normalized > 0.001)
        {
            DrawArc(ctx, cx, cy, arcR, MinAngle, valueAngle, arcBrush, thickness);
        }

        // ── 3) Ticks (solo si hay espacio suficiente) ────────────────────
        bool canShowTicks = ShowTicks && size >= 36;
        if (canShowTicks)
        {
            int tickCount = 11;
            double tickBase = arcR - thickness * 0.9;
            double tickLength = Math.Max(2.0, size * 0.055);
            var tickPen = new Pen(tickBrush, 1);

            for (int i = 0; i < tickCount; i++)
            {
                double t = (double)i / (tickCount - 1);
                double ang = MinAngle + t * SweepAngle;
                bool isMajor = (i % 5 == 0);
                double len = isMajor ? tickLength * 1.5 : tickLength;

                var p1 = PolarToPoint(cx, cy, tickBase, ang);
                var p2 = PolarToPoint(cx, cy, tickBase - len, ang);
                ctx.DrawLine(tickPen, p1, p2);
            }
        }

        // ── 4) Sombra bajo el knob (efecto 3D) ───────────────────────────
        var shadowBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
        ctx.DrawEllipse(shadowBrush, null,
            new Point(cx, cy + 1.2), knobR + 1.5, knobR + 1.5);

        // ── 5) Aro exterior del knob (rim) ───────────────────────────────
        ctx.DrawEllipse(rimBrush, null, new Point(cx, cy), knobR + 1.5, knobR + 1.5);

        // ── 6) Cuerpo del knob (face con gradiente) ──────────────────────
        ctx.DrawEllipse(faceBrush, null, new Point(cx, cy), knobR, knobR);

        // ── 7) Brillo superior (highlight) ───────────────────────────────
        var highlightBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0.6, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
            }
        };
        ctx.DrawEllipse(highlightBrush, null,
            new Point(cx, cy - knobR * 0.28), knobR * 0.72, knobR * 0.50);

        // ── 8) Aguja indicadora ──────────────────────────────────────────
        double pointerInner = knobR * 0.18;
        double pointerOuter = knobR * 0.82;
        double pointerWidth = Math.Max(1.5, size * 0.045);

        var innerP = PolarToPoint(cx, cy, pointerInner, valueAngle);
        var outerP = PolarToPoint(cx, cy, pointerOuter, valueAngle);

        var pointerPen = new Pen(pointerBrush, pointerWidth) { LineCap = PenLineCap.Round };
        ctx.DrawLine(pointerPen, innerP, outerP);

        // Tornillo central.
        var centerBrush = new SolidColorBrush(Color.Parse("#1A1F2A"));
        ctx.DrawEllipse(centerBrush, null, new Point(cx, cy), pointerInner * 0.85, pointerInner * 0.85);
    }

    private static IBrush CreateKnobFaceBrush()
    {
        return new RadialGradientBrush
        {
            Center = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#3C4456"), 0.0),
                new GradientStop(Color.Parse("#2A3040"), 0.55),
                new GradientStop(Color.Parse("#1A1E28"), 1.0)
            }
        };
    }

    // ───── Helpers de dibujo ─────────────────────────────────────────────

    /// -------------
    /// Convierte (radio, angulo) a un punto absoluto. El angulo se mide
    /// en grados respecto a la vertical (0 = top, positivo = clockwise).
    /// -------------
    private static Point PolarToPoint(double cx, double cy, double radius, double angleDeg)
    {
        double rad = (angleDeg - 90.0) * Math.PI / 180.0;
        return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
    }

    private static void DrawArc(
        DrawingContext ctx, double cx, double cy, double radius,
        double startAngle, double endAngle, IBrush brush, double thickness)
    {
        if (endAngle <= startAngle) return;

        var startP = PolarToPoint(cx, cy, radius, startAngle);
        var endP = PolarToPoint(cx, cy, radius, endAngle);

        bool isLargeArc = (endAngle - startAngle) > 180.0;

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(startP, false);
            gc.ArcTo(endP, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
            gc.EndFigure(false);
        }

        var pen = new Pen(brush, thickness) { LineCap = PenLineCap.Round };
        ctx.DrawGeometry(null, pen, geometry);
    }

    // ───── Interaccion ───────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var p = e.GetPosition(this);

        // Arrastre RELATIVO (como agarrar un knob real):
        // guardamos el angulo y el valor al momento de presionar y luego
        // aplicamos el delta de angulo respecto a ese punto.
        _pressAngle = AngleFromPoint(p);
        _pressValue = Value;
        _isDragging = true;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var p = e.GetPosition(this);
        double current = AngleFromPoint(p);

        double delta = current - _pressAngle;

        // Corregir wraparound (ej: de +170° a -170° son +20°, no -340°).
        if (delta > 180.0) delta -= 360.0;
        else if (delta < -180.0) delta += 360.0;

        double range = Math.Max(0.0001, Maximum - Minimum);
        double valueDelta = delta / SweepAngle * range;

        SetValueInternal(_pressValue + valueDelta);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        double step = Step > 0 ? Step : 0.01;
        double delta = e.Delta.Y * step;

        SetValueInternal(Value + delta);
        e.Handled = true;
    }

    // ───── Helpers de conversion ────────────────────────────────────────

    private void SetValueInternal(double newValue)
    {
        if (newValue < Minimum) newValue = Minimum;
        if (newValue > Maximum) newValue = Maximum;

        // Snap al Step (evita valores como 0.33333333).
        if (Step > 0)
        {
            double steps = Math.Round((newValue - Minimum) / Step);
            newValue = Minimum + steps * Step;

            if (newValue < Minimum) newValue = Minimum;
            if (newValue > Maximum) newValue = Maximum;
        }

        Value = newValue;
    }

    /// -------------
    /// Devuelve el angulo del punto respecto al centro del control,
    /// en el rango [-180, 180] y con 0 = arriba, positivo = clockwise.
    /// -------------
    private double AngleFromPoint(Point p)
    {
        double cx = Bounds.Width / 2;
        double cy = Bounds.Height / 2;

        double dx = p.X - cx;
        double dy = p.Y - cy;

        // atan2 devuelve [-π, π], con 0 = derecha, positivo = abajo.
        // Convertimos a "angulo respecto al top, positivo = clockwise".
        double atan2Deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double screenAngle = atan2Deg + 90.0;

        if (screenAngle > 180.0) screenAngle -= 360.0;
        if (screenAngle < -180.0) screenAngle += 360.0;

        return screenAngle;
    }

    private double GetNormalizedValue()
    {
        double range = Maximum - Minimum;
        if (range <= 0) return 0;
        return Math.Clamp((Value - Minimum) / range, 0.0, 1.0);
    }

    // ───── Medida ────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        // Queremos un cuadrado. Si el contenedor nos da espacio, tomamos
        // el menor lado. Si no, devolvemos 48x48 por defecto.
        double w = double.IsInfinity(availableSize.Width) ? 48 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 48 : availableSize.Height;
        double side = Math.Min(w, h);
        return new Size(side, side);
    }
}