using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Control que dibuja una linea de texto como display de 7 segmentos.
/// Soporta digitos 0-9, dos puntos (:), guion (-), punto (.) y espacio.
///
/// Los segmentos se dibujan a mano (formas hexagonales con extremos en
/// punta) para lograr el aspecto realista de un display LED.
///
/// Los colores por defecto son theme-aware:
///   - Dark:  segmentos encendidos cyan brillante, fondo oscuro.
///   - Light: segmentos encendidos negros, fondo cyan claro.
///
/// Se puede sobreescribir con OnBrush/OffBrush/InactiveBrush.
/// </summary>
public sealed class LedLine : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<LedLine, string>(nameof(Text), "00:00");

    public static readonly StyledProperty<IBrush?> OnBrushProperty =
        AvaloniaProperty.Register<LedLine, IBrush?>(nameof(OnBrush));

    public static readonly StyledProperty<IBrush?> OffBrushProperty =
        AvaloniaProperty.Register<LedLine, IBrush?>(nameof(OffBrush));

    public static readonly StyledProperty<double> SegmentThicknessProperty =
        AvaloniaProperty.Register<LedLine, double>(nameof(SegmentThickness), 0.20);

    public static readonly StyledProperty<double> CharacterSpacingProperty =
        AvaloniaProperty.Register<LedLine, double>(nameof(CharacterSpacing), 0.30);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LedLine, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<LedLine, IBrush?>(nameof(InactiveBrush));

    public static readonly StyledProperty<HorizontalAlignment> ContentAlignmentProperty =
        AvaloniaProperty.Register<LedLine, HorizontalAlignment>(nameof(ContentAlignment), HorizontalAlignment.Right);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? InactiveBrush
    {
        get => GetValue(InactiveBrushProperty);
        set => SetValue(InactiveBrushProperty, value);
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IBrush? OnBrush { get => GetValue(OnBrushProperty); set => SetValue(OnBrushProperty, value); }
    public IBrush? OffBrush { get => GetValue(OffBrushProperty); set => SetValue(OffBrushProperty, value); }
    public double SegmentThickness { get => GetValue(SegmentThicknessProperty); set => SetValue(SegmentThicknessProperty, value); }
    public double CharacterSpacing { get => GetValue(CharacterSpacingProperty); set => SetValue(CharacterSpacingProperty, value); }
    public HorizontalAlignment ContentAlignment
    {
        get => GetValue(ContentAlignmentProperty);
        set => SetValue(ContentAlignmentProperty, value);
    }

    static LedLine()
    {
        AffectsRender<LedLine>(
            TextProperty, OnBrushProperty, OffBrushProperty,
            SegmentThicknessProperty, CharacterSpacingProperty,
            ContentAlignmentProperty, IsActiveProperty, InactiveBrushProperty);
        AffectsMeasure<LedLine>(TextProperty, CharacterSpacingProperty);
    }

    public LedLine()
    {
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    // ───── Suscripcion al tema ───────────────────────────────────────────

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        InvalidateVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    // ───── Colores por defecto segun el tema ────────────────────────────
    //
    // Dark:  display LED clasico (cyan brillante sobre fondo oscuro).
    // Light: display LED invertido (negro sobre fondo cyan claro).

    private static Color GetDefaultOnColor() => ThemeService.IsDarkVariant
        ? Color.Parse("#00E5FF")   // cyan brillante
        : Color.Parse("#000000");  // negro

    private static Color GetDefaultInactiveColor() => ThemeService.IsDarkVariant
        ? Color.Parse("#0D3D4A")   // cyan tenue
        : Color.Parse("#7A9AA5");  // gris-cyan medio

    private static Color GetDefaultOffColor() => ThemeService.IsDarkVariant
        ? Color.Parse("#0A2A33")   // cyan muy oscuro
        : Color.Parse("#B8DCE5");  // cyan claro (segmentos apagados)

    // ───── Mapa de segmentos ─────────────────────────────────────────────

    // Bit A=1, B=2, C=4, D=8, E=16, F=32, G=64
    private static readonly Dictionary<char, int> SegmentMap = new()
    {
        ['0'] = 0x3F,
        ['1'] = 0x06,
        ['2'] = 0x5B,
        ['3'] = 0x4F,
        ['4'] = 0x66,
        ['5'] = 0x6D,
        ['6'] = 0x7D,
        ['7'] = 0x07,
        ['8'] = 0x7F,
        ['9'] = 0x6F,
        ['-'] = 0x40,
        ['_'] = 0x08,
        [' '] = 0x00,
    };

    private const double DigitWidthRatio = 0.62;
    private const double ColonWidthRatio = 0.22;
    private const double DotWidthRatio = 0.16;

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Height < 3) return;

        var text = (Text ?? string.Empty).ToUpperInvariant();
        if (text.Length == 0) return;

        double totalRel = 0;
        for (int i = 0; i < text.Length; i++)
            totalRel += CharWidthRatio(text[i]);
        totalRel += (text.Length - 1) * CharacterSpacing;
        if (totalRel <= 0) return;

        double digitHeight = bounds.Height;
        double digitUnit = digitHeight * DigitWidthRatio;
        double totalWidth = totalRel * digitUnit;

        double offsetX = ContentAlignment switch
        {
            HorizontalAlignment.Left => 0,
            HorizontalAlignment.Center => (bounds.Width - totalWidth) / 2,
            HorizontalAlignment.Right => bounds.Width - totalWidth,
            _ => bounds.Width - totalWidth,
        };
        if (offsetX < 0) offsetX = 0;

        // Brushes efectivos (tema si no fueron customizados).
        var defaultActive = new SolidColorBrush(GetDefaultOnColor());
        var defaultInactive = new SolidColorBrush(GetDefaultInactiveColor());

        IBrush onBrush = IsActive
            ? (OnBrush ?? defaultActive)
            : (InactiveBrush ?? defaultInactive);

        var offBrush = OffBrush ?? new SolidColorBrush(GetDefaultOffColor());

        double thickness = digitUnit * SegmentThickness;
        if (thickness < 1) thickness = 1;

        double cursor = offsetX;

        foreach (var ch in text)
        {
            double relW = CharWidthRatio(ch);
            double charW = relW * digitUnit;

            if (ch == ':')
            {
                DrawColon(ctx, cursor, 0, charW, digitHeight, onBrush);
            }
            else if (ch == '.')
            {
                DrawDot(ctx, cursor, 0, charW, digitHeight, onBrush);
            }
            else
            {
                int mask = SegmentMap.TryGetValue(ch, out var m) ? m : 0x00;
                DrawDigit(ctx, cursor, 0, charW, digitHeight, thickness, mask, onBrush, offBrush);
            }

            cursor += charW + CharacterSpacing * digitUnit;
        }
    }

    private static double CharWidthRatio(char c) => c switch
    {
        ':' => ColonWidthRatio,
        '.' => DotWidthRatio,
        _ => 1.0,
    };

    // ─────────────────────────────────────────────────────────────────────
    // Dibujo

    private static void DrawDigit(DrawingContext ctx,
                                  double x, double y, double w, double h,
                                  double t, int mask, IBrush on, IBrush off)
    {
        double pad = t * 0.15;
        double sx = x + pad;
        double sy = y + pad;
        double sw = w - pad * 2;
        double sh = h - pad * 2;
        double halfH = sh / 2;

        FillSeg(ctx, MakeHorizontalSegment(sx, sy, sx + sw, sy, t), (mask & 0x01) != 0, on, off);
        FillSeg(ctx, MakeVerticalSegment(sx + sw, sy, sx + sw, sy + halfH, t), (mask & 0x02) != 0, on, off);
        FillSeg(ctx, MakeVerticalSegment(sx + sw, sy + halfH, sx + sw, sy + sh, t), (mask & 0x04) != 0, on, off);
        FillSeg(ctx, MakeHorizontalSegment(sx, sy + sh, sx + sw, sy + sh, t), (mask & 0x08) != 0, on, off);
        FillSeg(ctx, MakeVerticalSegment(sx, sy + halfH, sx, sy + sh, t), (mask & 0x10) != 0, on, off);
        FillSeg(ctx, MakeVerticalSegment(sx, sy, sx, sy + halfH, t), (mask & 0x20) != 0, on, off);
        FillSeg(ctx, MakeHorizontalSegment(sx, sy + halfH, sx + sw, sy + halfH, t), (mask & 0x40) != 0, on, off);
    }

    private static void FillSeg(DrawingContext ctx, Geometry geo, bool isOn, IBrush on, IBrush off)
    {
        ctx.DrawGeometry(isOn ? on : off, null, geo);
    }

    private static Geometry MakeHorizontalSegment(double x1, double y1, double x2, double y2, double t)
    {
        double half = t / 2;
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(new Point(x1 + half, y1 - half), true);
        gc.LineTo(new Point(x2 - half, y2 - half));
        gc.LineTo(new Point(x2, y2));
        gc.LineTo(new Point(x2 - half, y2 + half));
        gc.LineTo(new Point(x1 + half, y1 + half));
        gc.LineTo(new Point(x1, y1));
        gc.EndFigure(true);
        return g;
    }

    private static Geometry MakeVerticalSegment(double x1, double y1, double x2, double y2, double t)
    {
        double half = t / 2;
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(new Point(x1, y1), true);
        gc.LineTo(new Point(x1 + half, y1 + half));
        gc.LineTo(new Point(x2 + half, y2 - half));
        gc.LineTo(new Point(x2, y2));
        gc.LineTo(new Point(x2 - half, y2 - half));
        gc.LineTo(new Point(x1 - half, y1 + half));
        gc.EndFigure(true);
        return g;
    }

    private static void DrawColon(DrawingContext ctx, double x, double y, double w, double h, IBrush brush)
    {
        double dotSize = w * 0.6;
        double cx = x + w / 2;
        ctx.DrawEllipse(brush, null, new Point(cx, y + h * 0.32), dotSize / 2, dotSize / 2);
        ctx.DrawEllipse(brush, null, new Point(cx, y + h * 0.68), dotSize / 2, dotSize / 2);
    }

    private static void DrawDot(DrawingContext ctx, double x, double y, double w, double h, IBrush brush)
    {
        double dotSize = w * 0.75;
        ctx.DrawEllipse(brush, null,
            new Point(x + w / 2, y + h - dotSize / 2),
            dotSize / 2, dotSize / 2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Medida

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? string.Empty;

        double totalRel = 0;
        for (int i = 0; i < text.Length; i++)
            totalRel += CharWidthRatio(text[i]);
        if (text.Length > 1) totalRel += (text.Length - 1) * CharacterSpacing;

        if (totalRel <= 0) return new Size(0, 0);

        double height = double.IsInfinity(availableSize.Height) ? 30 : availableSize.Height;
        double naturalWidth = height * DigitWidthRatio * totalRel;

        double finalWidth = naturalWidth;

        if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
        {
            if (availableSize.Width < naturalWidth)
            {
                finalWidth = availableSize.Width;
                height = finalWidth / (DigitWidthRatio * totalRel);
            }
            else
            {
                finalWidth = availableSize.Width;
            }
        }

        return new Size(finalWidth, height);
    }
}