using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RMWave.UI.Controls;

/// <summary>
/// Knob/perilla de audio profesional con arco de valor, interacción por
/// arrastre vertical y rueda del mouse.
/// </summary>
public class AudioKnob : Control
{
    // ==========================================
    // PROPIEDADES
    // ==========================================

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(Minimum), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(Maximum), 1.0);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(Value), 0.0);

    public static readonly StyledProperty<double> KnobSizeProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(KnobSize), 40.0);

    public static readonly StyledProperty<IBrush> BodyColorProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(BodyColor),
            new SolidColorBrush(Color.FromRgb(60, 60, 65)));

    public static readonly StyledProperty<IBrush> BodyHighlightProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(BodyHighlight),
            new SolidColorBrush(Color.FromRgb(90, 90, 95)));

    public static readonly StyledProperty<IBrush> IndicatorColorProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(IndicatorColor),
            Brushes.White);

    public static readonly StyledProperty<IBrush> TrackColorProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(TrackColor),
            new SolidColorBrush(Color.FromRgb(45, 45, 48)));

    public static readonly StyledProperty<IBrush> ValueArcColorProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(ValueArcColor),
            new SolidColorBrush(Color.FromRgb(0, 150, 220)));

    public static readonly StyledProperty<bool> ShowValueArcProperty =
        AvaloniaProperty.Register<AudioKnob, bool>(nameof(ShowValueArc), true);

    public static readonly StyledProperty<double> ArcThicknessProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(ArcThickness), 3.0);

    public static readonly StyledProperty<double> SensitivityProperty =
        AvaloniaProperty.Register<AudioKnob, double>(nameof(Sensitivity), 150.0);

    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<AudioKnob, string>(nameof(ValueFormat), "0.00");

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<AudioKnob, string>(nameof(Label), "");

    public static readonly StyledProperty<IBrush> LabelColorProperty =
        AvaloniaProperty.Register<AudioKnob, IBrush>(nameof(LabelColor),
            new SolidColorBrush(Color.FromRgb(180, 180, 180)));

    // Propiedades CLR
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value
    {
        get => GetValue(ValueProperty);
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(clamped - GetValue(ValueProperty)) > 1e-9)
            {
                SetValue(ValueProperty, clamped);
                OnValueChanged();
            }
        }
    }
    public double KnobSize { get => GetValue(KnobSizeProperty); set => SetValue(KnobSizeProperty, value); }
    public IBrush BodyColor { get => GetValue(BodyColorProperty); set => SetValue(BodyColorProperty, value); }
    public IBrush BodyHighlight { get => GetValue(BodyHighlightProperty); set => SetValue(BodyHighlightProperty, value); }
    public IBrush IndicatorColor { get => GetValue(IndicatorColorProperty); set => SetValue(IndicatorColorProperty, value); }
    public IBrush TrackColor { get => GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public IBrush ValueArcColor { get => GetValue(ValueArcColorProperty); set => SetValue(ValueArcColorProperty, value); }
    public bool ShowValueArc { get => GetValue(ShowValueArcProperty); set => SetValue(ShowValueArcProperty, value); }
    public double ArcThickness { get => GetValue(ArcThicknessProperty); set => SetValue(ArcThicknessProperty, value); }
    public double Sensitivity { get => GetValue(SensitivityProperty); set => SetValue(SensitivityProperty, value); }
    public string ValueFormat { get => GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public IBrush LabelColor { get => GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }

    /// <summary>Se dispara cuando el usuario cambia el valor.</summary>
    public event Action<double>? ValueChanged;

    // ==========================================
    // ESTADO INTERNO
    // ==========================================

    private bool _isDragging;
    private Point _dragStart;
    private double _dragStartValue;
    private const double StartAngleDeg = 135.0;   // abajo-izquierda
    private const double SweepDeg = 270.0;         // recorrido total

    static AudioKnob()
    {
        AffectsRender<AudioKnob>(
            MinimumProperty, MaximumProperty, ValueProperty, KnobSizeProperty,
            BodyColorProperty, BodyHighlightProperty, IndicatorColorProperty,
            TrackColorProperty, ValueArcColorProperty, ShowValueArcProperty,
            ArcThicknessProperty, LabelProperty, LabelColorProperty);
    }

    public AudioKnob()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
    }

    private void OnValueChanged()
    {
        ValueChanged?.Invoke(Value);
        InvalidateVisual();
        // Actualizar tooltip
        var text = Value.ToString(ValueFormat);
        if (!string.IsNullOrEmpty(Label))
            text = $"{Label}: {text}";
        ToolTip.SetTip(this, text);
    }

    // ==========================================
    // LAYOUT
    // ==========================================

    protected override Size MeasureOverride(Size availableSize)
    {
        double arcRadius = KnobSize / 2 + ArcThickness + 2;
        double size = arcRadius * 2 + 4;
        return new Size(
            double.IsInfinity(availableSize.Width) ? size : Math.Min(size, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? size : Math.Min(size, availableSize.Height));
    }

    // ==========================================
    // INTERACCIÓN
    // ==========================================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var pos = e.GetPosition(this);
        double deltaY = _dragStart.Y - pos.Y; // arriba = positivo
        double range = Maximum - Minimum;
        if (range <= 0) return;

        double deltaValue = deltaY / Sensitivity * range;
        Value = Math.Clamp(_dragStartValue + deltaValue, Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double range = Maximum - Minimum;
        if (range <= 0) return;

        double step = range / 40.0; // 40 pasos para recorrer todo el rango
        Value = Math.Clamp(Value + e.Delta.Y * step, Minimum, Maximum);
        e.Handled = true;
    }

    // ==========================================
    // RENDER
    // ==========================================

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double arcRadius = KnobSize / 2 + ArcThickness + 2;
        double totalSize = Math.Min(Bounds.Width, Bounds.Height);
        double cx = Bounds.Width / 2;
        double cy = Bounds.Height / 2;
        double knobRadius = KnobSize / 2;

        // Escalar si el espacio es menor.
        double scale = totalSize < arcRadius * 2 + 4
            ? totalSize / (arcRadius * 2 + 4)
            : 1.0;
        arcRadius *= scale;
        knobRadius *= scale;

        double fraction = (Maximum - Minimum) > 0
            ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
            : 0;
        double currentAngle = StartAngleDeg + fraction * SweepDeg;

        // 1) Arco de fondo (track).
        if (ShowValueArc)
        {
            var trackGeo = CreateArc(cx, cy, arcRadius, StartAngleDeg, StartAngleDeg + SweepDeg);
            ctx.DrawGeometry(null, new Pen(TrackColor, ArcThickness * scale), trackGeo);

            // 2) Arco de valor.
            if (fraction > 0.001)
            {
                var valueGeo = CreateArc(cx, cy, arcRadius, StartAngleDeg, currentAngle);
                ctx.DrawGeometry(null, new Pen(ValueArcColor, ArcThickness * scale), valueGeo);
            }
        }

        // 3) Cuerpo del knob con gradiente radial (efecto 3D).
        var bodyGradient = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new GradientStop(GetColor(BodyHighlight), 0),
                new GradientStop(GetColor(BodyColor), 0.9),
                new GradientStop(GetColor(BodyColor), 1)
            },
            GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };

        ctx.DrawEllipse(bodyGradient,
            new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 38)), 1.5 * scale),
            new Point(cx, cy), knobRadius, knobRadius);

        // 4) Línea indicadora (desde el centro hacia el borde).
        double indicatorStart = knobRadius * 0.25;
        double indicatorEnd = knobRadius * 0.82;
        var indStart = PointOnCircle(cx, cy, indicatorStart, currentAngle);
        var indEnd = PointOnCircle(cx, cy, indicatorEnd, currentAngle);

        var indicatorPen = new Pen(IndicatorColor, Math.Max(1.5, 2.5 * scale));
        indicatorPen.LineCap = PenLineCap.Round;
        ctx.DrawLine(indicatorPen, indStart, indEnd);

        // 5) Punto central.
        ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(40, 40, 42)), null,
            new Point(cx, cy), knobRadius * 0.18, knobRadius * 0.18);
    }

    // ==========================================
    // HELPERS GEOMÉTRICOS
    // ==========================================

    private static Point PointOnCircle(double cx, double cy, double radius, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
    }

    private static StreamGeometry CreateArc(double cx, double cy, double radius,
        double startAngle, double endAngle)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var start = PointOnCircle(cx, cy, radius, startAngle);
            var end = PointOnCircle(cx, cy, radius, endAngle);
            double sweep = endAngle - startAngle;
            bool isLargeArc = Math.Abs(sweep) > 180;

            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }

    private static Color GetColor(IBrush brush) =>
        brush is ISolidColorBrush s ? s.Color : Colors.Gray;
}
