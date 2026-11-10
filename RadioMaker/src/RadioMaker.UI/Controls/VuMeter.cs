using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RadioMaker.UI.Services;
using SkiaSharp;
using System.Globalization;

namespace RadioMaker.UI.Controls;

public enum VuMeterStyle { Console, LedSegments, Minimal }
public enum VuMeterOrientation { Vertical, Horizontal }

/// <summary>
/// Control de VU meter personalizable: 3 estilos, 2 orientaciones, todo bindable.
///
/// El color de fondo (BackgroundColor) es theme-aware cuando NO se setea
/// explicitamente:
///   - Dark:  #1A1A1A (barra oscura, clasico).
///   - Light: #C8E0E8 (barra clara, para fondo claro).
///
/// Si el usuario setea BackgroundColor en XAML, se respeta y no se adapta.
/// </summary>
public sealed class VuMeter : Control
{
    public static readonly StyledProperty<VuMeterStyle> StyleProperty =
        AvaloniaProperty.Register<VuMeter, VuMeterStyle>(nameof(Style), VuMeterStyle.LedSegments);
    public static readonly StyledProperty<VuMeterOrientation> OrientationProperty =
        AvaloniaProperty.Register<VuMeter, VuMeterOrientation>(nameof(Orientation), VuMeterOrientation.Vertical);
    public static readonly StyledProperty<bool> ShowDbScaleProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowDbScale), true);
    public static readonly StyledProperty<int> SegmentCountProperty =
        AvaloniaProperty.Register<VuMeter, int>(nameof(SegmentCount), 24);
    public static readonly StyledProperty<bool> ShowPeakHoldProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowPeakHold), true);
    public static readonly StyledProperty<bool> ShowRmsProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowRms), false);
    public static readonly StyledProperty<bool> ShowClipIndicatorProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowClipIndicator), true);
    public static readonly StyledProperty<double> BarWidthProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(BarWidth), 26.0);

    public static readonly StyledProperty<Color> GreenZoneColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(GreenZoneColor), Color.Parse("#10B981"));
    public static readonly StyledProperty<Color> AmberZoneColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(AmberZoneColor), Color.Parse("#F59E0B"));
    public static readonly StyledProperty<Color> RedZoneColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(RedZoneColor), Color.Parse("#EF4444"));
    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(BackgroundColor), Color.Parse("#1A1A1A"));
    public static readonly StyledProperty<Color> ScaleTextColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(ScaleTextColor), Color.Parse("#9CA3AF"));
    public static readonly StyledProperty<Color> AccentColorProperty =
        AvaloniaProperty.Register<VuMeter, Color>(nameof(AccentColor), Color.Parse("#4D6BFE"));

    public static readonly StyledProperty<double> LeftLevelProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(LeftLevel));
    public static readonly StyledProperty<double> RightLevelProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(RightLevel));
    public static readonly StyledProperty<double> LeftPeakProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(LeftPeak));
    public static readonly StyledProperty<double> RightPeakProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(RightPeak));
    public static readonly StyledProperty<double> LeftRmsProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(LeftRms));
    public static readonly StyledProperty<double> RightRmsProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(RightRms));
    public static readonly StyledProperty<bool> ShowClipProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowClip));

    public VuMeterStyle Style { get => GetValue(StyleProperty); set => SetValue(StyleProperty, value); }
    public VuMeterOrientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool ShowDbScale { get => GetValue(ShowDbScaleProperty); set => SetValue(ShowDbScaleProperty, value); }
    public int SegmentCount { get => GetValue(SegmentCountProperty); set => SetValue(SegmentCountProperty, value); }
    public bool ShowPeakHold { get => GetValue(ShowPeakHoldProperty); set => SetValue(ShowPeakHoldProperty, value); }
    public bool ShowRms { get => GetValue(ShowRmsProperty); set => SetValue(ShowRmsProperty, value); }
    public bool ShowClipIndicator { get => GetValue(ShowClipIndicatorProperty); set => SetValue(ShowClipIndicatorProperty, value); }
    public double BarWidth { get => GetValue(BarWidthProperty); set => SetValue(BarWidthProperty, value); }
    public Color GreenZoneColor { get => GetValue(GreenZoneColorProperty); set => SetValue(GreenZoneColorProperty, value); }
    public Color AmberZoneColor { get => GetValue(AmberZoneColorProperty); set => SetValue(AmberZoneColorProperty, value); }
    public Color RedZoneColor { get => GetValue(RedZoneColorProperty); set => SetValue(RedZoneColorProperty, value); }
    public Color BackgroundColor { get => GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }
    public Color ScaleTextColor { get => GetValue(ScaleTextColorProperty); set => SetValue(ScaleTextColorProperty, value); }
    public Color AccentColor { get => GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public double LeftLevel { get => GetValue(LeftLevelProperty); set => SetValue(LeftLevelProperty, value); }
    public double RightLevel { get => GetValue(RightLevelProperty); set => SetValue(RightLevelProperty, value); }
    public double LeftPeak { get => GetValue(LeftPeakProperty); set => SetValue(LeftPeakProperty, value); }
    public double RightPeak { get => GetValue(RightPeakProperty); set => SetValue(RightPeakProperty, value); }
    public double LeftRms { get => GetValue(LeftRmsProperty); set => SetValue(LeftRmsProperty, value); }
    public double RightRms { get => GetValue(RightRmsProperty); set => SetValue(RightRmsProperty, value); }
    public bool ShowClip { get => GetValue(ShowClipProperty); set => SetValue(ShowClipProperty, value); }

    private static readonly int[] ScaleMarks = { 0, -6, -12, -24, -48, -60 };
    private const double MinDb = -60.0;

    private bool _subscribed;

    static VuMeter()
    {
        AffectsRender<VuMeter>(
            StyleProperty, OrientationProperty, ShowDbScaleProperty, SegmentCountProperty,
            ShowPeakHoldProperty, ShowRmsProperty, ShowClipIndicatorProperty, BarWidthProperty,
            GreenZoneColorProperty, AmberZoneColorProperty, RedZoneColorProperty,
            BackgroundColorProperty, ScaleTextColorProperty, AccentColorProperty,
            LeftLevelProperty, RightLevelProperty, LeftPeakProperty, RightPeakProperty,
            LeftRmsProperty, RightRmsProperty, ShowClipProperty);
    }

    public VuMeter()
    {
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    // ───── Tema ──────────────────────────────────────────────────────────

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed) return;
        ThemeService.ThemeChanged += OnThemeChanged;
        _subscribed = true;
        InvalidateVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_subscribed) return;
        ThemeService.ThemeChanged -= OnThemeChanged;
        _subscribed = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    /// <summary>
    /// Devuelve el color de fondo efectivo de las barras.
    /// Si el usuario lo seteo explicitamente, se respeta.
    /// Si no, se elige segun el tema.
    /// </summary>
    private Color GetEffectiveBackground()
    {
        if (IsSet(BackgroundColorProperty))
            return BackgroundColor;

        return ThemeService.IsDarkVariant
            ? Color.Parse("#1A1A1A")
            : Color.Parse("#C8E0E8");
    }

    /// <summary>
    /// Devuelve el color efectivo del texto de la escala.
    /// </summary>
    private Color GetEffectiveScaleText()
    {
        if (IsSet(ScaleTextColorProperty))
            return ScaleTextColor;

        return ThemeService.IsDarkVariant
            ? Color.Parse("#9CA3AF")
            : Color.Parse("#4B5563");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation == VuMeterOrientation.Vertical)
        {
            double scale = ShowDbScale ? 32 : 0;
            double w = scale + BarWidth * 2 + 8;
            double h = double.IsInfinity(availableSize.Height) || availableSize.Height < 100 ? 200 : availableSize.Height;
            return new Size(w, h);
        }
        else
        {
            double scale = ShowDbScale ? 20 : 0;
            double w = double.IsInfinity(availableSize.Width) || availableSize.Width < 200 ? 300 : availableSize.Width;
            double h = scale + BarWidth * 2 + 12;
            return new Size(w, h);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 10 || bounds.Height < 10) return;

        if (Orientation == VuMeterOrientation.Vertical) RenderVertical(context, bounds);
        else RenderHorizontal(context, bounds);
    }

    private void RenderVertical(DrawingContext ctx, Rect bounds)
    {
        double scaleWidth = ShowDbScale ? 32 : 0;
        double barAreaX = bounds.X + scaleWidth;
        double barAreaWidth = bounds.Width - scaleWidth;
        double barAreaTop = bounds.Y;
        double barAreaHeight = bounds.Height;
        if (ShowClipIndicator) barAreaHeight -= 18;

        double barGap = 6;
        double totalBarsWidth = BarWidth * 2 + barGap;
        double startX = barAreaX + (barAreaWidth - totalBarsWidth) / 2;

        var barRectL = new Rect(startX, barAreaTop, BarWidth, barAreaHeight);
        var barRectR = new Rect(startX + BarWidth + barGap, barAreaTop, BarWidth, barAreaHeight);

        if (ShowDbScale) DrawScaleVertical(ctx, bounds.Y, bounds.Height - (ShowClipIndicator ? 18 : 0), bounds.X);

        switch (Style)
        {
            case VuMeterStyle.Console:
                DrawBarConsoleVertical(ctx, barRectL, LeftLevel, LeftPeak);
                DrawBarConsoleVertical(ctx, barRectR, RightLevel, RightPeak);
                break;
            case VuMeterStyle.LedSegments:
                DrawBarLedsVertical(ctx, barRectL, LeftLevel, LeftPeak);
                DrawBarLedsVertical(ctx, barRectR, RightLevel, RightPeak);
                break;
            case VuMeterStyle.Minimal:
                DrawBarMinimalVertical(ctx, barRectL, LeftLevel);
                DrawBarMinimalVertical(ctx, barRectR, RightLevel);
                break;
        }

        if (ShowClipIndicator && ShowClip)
            DrawClipBadge(ctx, bounds.X + scaleWidth + (barAreaWidth - 60) / 2, bounds.Bottom - 16);
    }

    private void RenderHorizontal(DrawingContext ctx, Rect bounds)
    {
        double scaleHeight = ShowDbScale ? 20 : 0;
        double barAreaHeight = bounds.Height - scaleHeight - (ShowClipIndicator ? 18 : 0);
        double barGap = 4;
        double barHeight = (barAreaHeight - barGap) / 2;
        if (barHeight < 8) barHeight = 8;

        var barRectL = new Rect(bounds.X, bounds.Y, bounds.Width, barHeight);
        var barRectR = new Rect(bounds.X, bounds.Y + barHeight + barGap, bounds.Width, barHeight);

        if (ShowDbScale) DrawScaleHorizontal(ctx, barRectR.Bottom + 4, bounds.X, bounds.Width);

        switch (Style)
        {
            case VuMeterStyle.Console:
                DrawBarConsoleHorizontal(ctx, barRectL, LeftLevel, LeftPeak);
                DrawBarConsoleHorizontal(ctx, barRectR, RightLevel, RightPeak);
                break;
            case VuMeterStyle.LedSegments:
                DrawBarLedsHorizontal(ctx, barRectL, LeftLevel, LeftPeak);
                DrawBarLedsHorizontal(ctx, barRectR, RightLevel, RightPeak);
                break;
            case VuMeterStyle.Minimal:
                DrawBarMinimalHorizontal(ctx, barRectL, LeftLevel);
                DrawBarMinimalHorizontal(ctx, barRectR, RightLevel);
                break;
        }

        if (ShowClipIndicator && ShowClip)
            DrawClipBadge(ctx, bounds.Right - 62, bounds.Bottom - 16);
    }

    private void DrawBarConsoleVertical(DrawingContext ctx, Rect barRect, double level, double peak)
    {
        var bgColor = GetEffectiveBackground();
        ctx.FillRectangle(new SolidColorBrush(bgColor), barRect);
        double pct = DbToPercent(LevelToDb(level));
        double activeH = barRect.Height * pct;
        var activeRect = new Rect(barRect.X, barRect.Bottom - activeH, barRect.Width, activeH);
        using (ctx.PushClip(activeRect))
            ctx.FillRectangle(CreateVerticalGradient(), barRect);

        if (ShowPeakHold && peak > 0.001)
        {
            double peakY = barRect.Bottom - barRect.Height * DbToPercent(LevelToDb(peak));
            ctx.DrawLine(new Pen(Brushes.White, 2), new Point(barRect.X, peakY), new Point(barRect.Right, peakY));
        }
        if (ShowRms)
        {
            double rmsY = barRect.Bottom - barRect.Height * DbToPercent(LevelToDb(LeftRms));
            ctx.DrawLine(new Pen(new SolidColorBrush(Colors.White, 0.6), 1, dashStyle: DashStyle.Dash),
                new Point(barRect.X, rmsY), new Point(barRect.Right, rmsY));
        }
    }

    private void DrawBarConsoleHorizontal(DrawingContext ctx, Rect barRect, double level, double peak)
    {
        var bgColor = GetEffectiveBackground();
        ctx.FillRectangle(new SolidColorBrush(bgColor), barRect);
        double pct = DbToPercent(LevelToDb(level));
        double activeW = barRect.Width * pct;
        var activeRect = new Rect(barRect.X, barRect.Y, activeW, barRect.Height);
        using (ctx.PushClip(activeRect))
            ctx.FillRectangle(CreateHorizontalGradient(), barRect);

        if (ShowPeakHold && peak > 0.001)
        {
            double peakX = barRect.X + barRect.Width * DbToPercent(LevelToDb(peak));
            ctx.DrawLine(new Pen(Brushes.White, 2), new Point(peakX, barRect.Y), new Point(peakX, barRect.Bottom));
        }
    }

    private IBrush CreateVerticalGradient() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(GreenZoneColor, 0.0),
            new GradientStop(GreenZoneColor, 0.65),
            new GradientStop(AmberZoneColor, 0.65),
            new GradientStop(AmberZoneColor, 0.85),
            new GradientStop(RedZoneColor, 0.85),
            new GradientStop(RedZoneColor, 1.0)
        }
    };

    private IBrush CreateHorizontalGradient() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(GreenZoneColor, 0.0),
            new GradientStop(GreenZoneColor, 0.65),
            new GradientStop(AmberZoneColor, 0.65),
            new GradientStop(AmberZoneColor, 0.85),
            new GradientStop(RedZoneColor, 0.85),
            new GradientStop(RedZoneColor, 1.0)
        }
    };

    private void DrawBarLedsVertical(DrawingContext ctx, Rect barRect, double level, double peak)
    {
        var bgColor = GetEffectiveBackground();
        int segments = Math.Max(4, SegmentCount);
        double gap = 2.5;
        double segHeight = (barRect.Height - gap * (segments - 1)) / segments;
        double pct = DbToPercent(LevelToDb(level));
        int activeCount = (int)Math.Round(pct * segments);
        int peakIndex = (int)Math.Round(DbToPercent(LevelToDb(peak)) * segments) - 1;

        for (int i = 0; i < segments; i++)
        {
            double y = barRect.Bottom - (i + 1) * segHeight - i * gap;
            var segRect = new Rect(barRect.X, y, barRect.Width, segHeight);
            Color color;
            if (i < activeCount)
            {
                double ratio = (double)i / (segments - 1);
                color = GetZoneColor(ratio);
            }
            else if (ShowPeakHold && i == peakIndex && peak > 0.001) color = Colors.White;
            else color = BlendWith(bgColor, Colors.White, 0.06);

            ctx.FillRectangle(new SolidColorBrush(color), segRect, 1.5f);
        }
    }

    private void DrawBarLedsHorizontal(DrawingContext ctx, Rect barRect, double level, double peak)
    {
        var bgColor = GetEffectiveBackground();
        int segments = Math.Max(4, SegmentCount);
        double gap = 2.5;
        double segWidth = (barRect.Width - gap * (segments - 1)) / segments;
        double pct = DbToPercent(LevelToDb(level));
        int activeCount = (int)Math.Round(pct * segments);
        int peakIndex = (int)Math.Round(DbToPercent(LevelToDb(peak)) * segments) - 1;

        for (int i = 0; i < segments; i++)
        {
            double x = barRect.X + i * (segWidth + gap);
            var segRect = new Rect(x, barRect.Y, segWidth, barRect.Height);
            Color color;
            if (i < activeCount)
            {
                double ratio = (double)i / (segments - 1);
                color = GetZoneColor(ratio);
            }
            else if (ShowPeakHold && i == peakIndex && peak > 0.001) color = Colors.White;
            else color = BlendWith(bgColor, Colors.White, 0.06);

            ctx.FillRectangle(new SolidColorBrush(color), segRect, 1.5f);
        }
    }

    private void DrawBarMinimalVertical(DrawingContext ctx, Rect barRect, double level)
    {
        var bgColor = GetEffectiveBackground();
        var trackRadius = barRect.Width / 2;
        ctx.FillRectangle(new SolidColorBrush(BlendWith(bgColor, Colors.White, 0.5)), barRect, (float)trackRadius);
        double pct = DbToPercent(LevelToDb(level));
        double activeH = barRect.Height * pct;
        if (activeH < 1) return;
        var fillRect = new Rect(barRect.X, barRect.Bottom - activeH, barRect.Width, activeH);
        ctx.FillRectangle(new SolidColorBrush(GetMinimalColor(pct)), fillRect, (float)trackRadius);
    }

    private void DrawBarMinimalHorizontal(DrawingContext ctx, Rect barRect, double level)
    {
        var bgColor = GetEffectiveBackground();
        var trackRadius = barRect.Height / 2;
        ctx.FillRectangle(new SolidColorBrush(BlendWith(bgColor, Colors.White, 0.5)), barRect, (float)trackRadius);
        double pct = DbToPercent(LevelToDb(level));
        double activeW = barRect.Width * pct;
        if (activeW < 1) return;
        var fillRect = new Rect(barRect.X, barRect.Y, activeW, barRect.Height);
        ctx.FillRectangle(new SolidColorBrush(GetMinimalColor(pct)), fillRect, (float)trackRadius);
    }

    private Color GetMinimalColor(double pct)
    {
        if (pct >= 0.92) return RedZoneColor;
        if (pct >= 0.78) return AmberZoneColor;
        return AccentColor;
    }

    private void DrawScaleVertical(DrawingContext ctx, double top, double height, double x)
    {
        var scaleColor = GetEffectiveScaleText();
        var brush = new SolidColorBrush(scaleColor);
        var typeface = new Typeface(FontFamily.Default);
        foreach (var db in ScaleMarks)
        {
            double pct = DbToPercent(db);
            double y = top + height * (1.0 - pct);
            var ft = new FormattedText(db.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 9, brush);
            ctx.DrawText(ft, new Point(x + 4, y - 5));
            ctx.DrawLine(new Pen(brush, 1), new Point(x + 26, y), new Point(x + 30, y));
        }
    }

    private void DrawScaleHorizontal(DrawingContext ctx, double y, double x, double width)
    {
        var scaleColor = GetEffectiveScaleText();
        var brush = new SolidColorBrush(scaleColor);
        var typeface = new Typeface(FontFamily.Default);
        foreach (var db in ScaleMarks)
        {
            double px = x + width * DbToPercent(db);
            var ft = new FormattedText(db.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 9, brush);
            ctx.DrawText(ft, new Point(px - ft.Width / 2, y));
        }
    }

    private void DrawClipBadge(DrawingContext ctx, double x, double y)
    {
        var rect = new Rect(x, y, 60, 14);
        ctx.FillRectangle(new SolidColorBrush(RedZoneColor), rect, 3);
        var ft = new FormattedText("CLIP", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, Brushes.White);
        ctx.DrawText(ft, new Point(x + (60 - ft.Width) / 2, y + 1));
    }

    private static double LevelToDb(double level)
    {
        if (level <= 0.001) return MinDb;
        double db = 20 * Math.Log10(level);
        return Math.Max(MinDb, db);
    }

    private static double DbToPercent(double db)
    {
        if (db >= 0) return 1.0;
        if (db <= MinDb) return 0.0;
        if (db >= -20.0) return 0.5 + (db + 20.0) / 20.0 * 0.5;
        return (db + 60.0) / 40.0 * 0.5;
    }

    private Color GetZoneColor(double ratio)
    {
        if (ratio >= 0.85) return RedZoneColor;
        if (ratio >= 0.65) return AmberZoneColor;
        return GreenZoneColor;
    }

    private static Color BlendWith(Color a, Color b, double t) =>
        Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}