using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RMWave.UI.Localization;

namespace RMWave.UI.Controls
{
    public enum VuOrientation
    {
        Vertical,
        Horizontal
    }

    /// <summary>Estilo visual del VU meter, seleccionable con click derecho sobre el control.</summary>
    public enum VuDisplayMode
    {
        /// <summary>Barra continua con degradé (el original).</summary>
        Solid,
        /// <summary>Barra en segmentos discretos, estilo VU de hardware de radio.</summary>
        Segmented,
        /// <summary>Aguja analógica clásica, una por canal.</summary>
        Analog
    }

    /// <summary>
    /// VU meter estereo (L/R) personalizable en color, tamaño y orientacion,
    /// con texto de nivel en dB a cada lado y peak-hold opcional.
    /// No depende de ManagedBass: solo expone LeftLevelDb / RightLevelDb (o SetLevels).
    /// La logica de lectura de audio se conecta desde afuera.
    /// </summary>
    public class VuMeter : Control
    {
        // ---------- Propiedades ----------

        public static readonly StyledProperty<VuOrientation> OrientationProperty =
            AvaloniaProperty.Register<VuMeter, VuOrientation>(nameof(Orientation), VuOrientation.Vertical);

        public static readonly StyledProperty<double> LeftLevelDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(LeftLevelDb), -60.0);

        public static readonly StyledProperty<double> RightLevelDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(RightLevelDb), -60.0);

        public static readonly StyledProperty<double> MinDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(MinDb), -60.0);

        public static readonly StyledProperty<double> MaxDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(MaxDb), 0.0);

        public static readonly StyledProperty<double> WarningDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(WarningDb), -12.0);

        public static readonly StyledProperty<double> DangerDbProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(DangerDb), -3.0);

        public static readonly StyledProperty<IBrush> LowColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(LowColor), Brushes.LimeGreen);

        public static readonly StyledProperty<IBrush> MidColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(MidColor), Brushes.Gold);

        public static readonly StyledProperty<IBrush> HighColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(HighColor), Brushes.Red);

        public static readonly StyledProperty<IBrush> TrackColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(TrackColor),
                new SolidColorBrush(Color.FromRgb(28, 28, 30)));

        public static readonly StyledProperty<IBrush> PeakColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(PeakColor), Brushes.White);

        public static readonly StyledProperty<IBrush> LabelColorProperty =
            AvaloniaProperty.Register<VuMeter, IBrush>(nameof(LabelColor), Brushes.Gainsboro);

        public static readonly StyledProperty<bool> ShowDbTextProperty =
            AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowDbText), true);

        public static readonly StyledProperty<bool> ShowPeakHoldProperty =
            AvaloniaProperty.Register<VuMeter, bool>(nameof(ShowPeakHold), true);

        public static readonly StyledProperty<double> BarThicknessProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(BarThickness), 18.0);

        public static readonly StyledProperty<double> BarSpacingProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(BarSpacing), 6.0);

        public static readonly StyledProperty<double> CornerRadiusProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(CornerRadius), 3.0);

        public static readonly StyledProperty<double> LabelFontSizeProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(LabelFontSize), 11.0);

        public static readonly StyledProperty<double> PeakHoldDecayDbPerSecondProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(PeakHoldDecayDbPerSecond), 20.0);

        public static readonly StyledProperty<int> PeakHoldTimeMsProperty =
            AvaloniaProperty.Register<VuMeter, int>(nameof(PeakHoldTimeMs), 1200);

        public static readonly StyledProperty<VuDisplayMode> DisplayModeProperty =
            AvaloniaProperty.Register<VuMeter, VuDisplayMode>(nameof(DisplayMode), VuDisplayMode.Solid);

        public static readonly StyledProperty<int> SegmentCountProperty =
            AvaloniaProperty.Register<VuMeter, int>(nameof(SegmentCount), 40);

        public static readonly StyledProperty<double> SegmentGapProperty =
            AvaloniaProperty.Register<VuMeter, double>(nameof(SegmentGap), 1.0);

        public VuOrientation Orientation
        {
            get => GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        public double LeftLevelDb
        {
            get => GetValue(LeftLevelDbProperty);
            set => SetValue(LeftLevelDbProperty, value);
        }

        public double RightLevelDb
        {
            get => GetValue(RightLevelDbProperty);
            set => SetValue(RightLevelDbProperty, value);
        }

        public double MinDb
        {
            get => GetValue(MinDbProperty);
            set => SetValue(MinDbProperty, value);
        }

        public double MaxDb
        {
            get => GetValue(MaxDbProperty);
            set => SetValue(MaxDbProperty, value);
        }

        public double WarningDb
        {
            get => GetValue(WarningDbProperty);
            set => SetValue(WarningDbProperty, value);
        }

        public double DangerDb
        {
            get => GetValue(DangerDbProperty);
            set => SetValue(DangerDbProperty, value);
        }

        public IBrush LowColor
        {
            get => GetValue(LowColorProperty);
            set => SetValue(LowColorProperty, value);
        }

        public IBrush MidColor
        {
            get => GetValue(MidColorProperty);
            set => SetValue(MidColorProperty, value);
        }

        public IBrush HighColor
        {
            get => GetValue(HighColorProperty);
            set => SetValue(HighColorProperty, value);
        }

        public IBrush TrackColor
        {
            get => GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        public IBrush PeakColor
        {
            get => GetValue(PeakColorProperty);
            set => SetValue(PeakColorProperty, value);
        }

        public IBrush LabelColor
        {
            get => GetValue(LabelColorProperty);
            set => SetValue(LabelColorProperty, value);
        }

        public bool ShowDbText
        {
            get => GetValue(ShowDbTextProperty);
            set => SetValue(ShowDbTextProperty, value);
        }

        public bool ShowPeakHold
        {
            get => GetValue(ShowPeakHoldProperty);
            set => SetValue(ShowPeakHoldProperty, value);
        }

        public double BarThickness
        {
            get => GetValue(BarThicknessProperty);
            set => SetValue(BarThicknessProperty, value);
        }

        public double BarSpacing
        {
            get => GetValue(BarSpacingProperty);
            set => SetValue(BarSpacingProperty, value);
        }

        public double CornerRadius
        {
            get => GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public double LabelFontSize
        {
            get => GetValue(LabelFontSizeProperty);
            set => SetValue(LabelFontSizeProperty, value);
        }

        public double PeakHoldDecayDbPerSecond
        {
            get => GetValue(PeakHoldDecayDbPerSecondProperty);
            set => SetValue(PeakHoldDecayDbPerSecondProperty, value);
        }

        public int PeakHoldTimeMs
        {
            get => GetValue(PeakHoldTimeMsProperty);
            set => SetValue(PeakHoldTimeMsProperty, value);
        }

        public VuDisplayMode DisplayMode
        {
            get => GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        public int SegmentCount
        {
            get => GetValue(SegmentCountProperty);
            set => SetValue(SegmentCountProperty, value);
        }

        public double SegmentGap
        {
            get => GetValue(SegmentGapProperty);
            set => SetValue(SegmentGapProperty, value);
        }

        // ---------- Estado interno (peak hold) ----------

        private double _leftPeakDb;
        private double _rightPeakDb;
        private DateTime _leftPeakTime;
        private DateTime _rightPeakTime;
        private readonly DispatcherTimer _peakTimer;

        static VuMeter()
        {
            AffectsRender<VuMeter>(
                OrientationProperty, LeftLevelDbProperty, RightLevelDbProperty,
                MinDbProperty, MaxDbProperty, WarningDbProperty, DangerDbProperty,
                LowColorProperty, MidColorProperty, HighColorProperty,
                TrackColorProperty, PeakColorProperty, LabelColorProperty,
                ShowDbTextProperty, ShowPeakHoldProperty,
                BarThicknessProperty, BarSpacingProperty, CornerRadiusProperty,
                LabelFontSizeProperty, DisplayModeProperty, SegmentCountProperty, SegmentGapProperty);
        }

        public VuMeter()
        {
            _leftPeakDb = MinDb;
            _rightPeakDb = MinDb;
            _leftPeakTime = DateTime.MinValue;
            _rightPeakTime = DateTime.MinValue;

            _peakTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _peakTimer.Tick += (_, __) => UpdatePeakDecay();

            BuildViewMenu();
        }

        /// <summary>Menú "Vista": click derecho sobre el VU para elegir Sólido / Segmentado / Analógico.</summary>
        private void BuildViewMenu()
        {
            var menu = new ContextMenu();
            var viewItem = new MenuItem { Header = WaveLocalizationService.GetString("Loc.RMWave.VuMeter.View") };

            void AddOption(string header, VuDisplayMode mode)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => DisplayMode = mode;
                viewItem.Items.Add(item);
            }

            AddOption(WaveLocalizationService.GetString("Loc.RMWave.VuMeter.Solid"), VuDisplayMode.Solid);
            AddOption(WaveLocalizationService.GetString("Loc.RMWave.VuMeter.Segmented"), VuDisplayMode.Segmented);
            AddOption(WaveLocalizationService.GetString("Loc.RMWave.VuMeter.Analog"), VuDisplayMode.Analog);

            menu.Items.Add(viewItem);
            ContextMenu = menu;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _peakTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _peakTimer.Stop();
        }

        /// <summary>
        /// Actualiza ambos canales de una sola vez (clampeando al rango Min/Max).
        /// Es el metodo que se llama desde el motor de audio (ManagedBass) periodicamente.
        /// </summary>
        public void SetLevels(double leftDb, double rightDb)
        {
            LeftLevelDb = Clamp(leftDb, MinDb, MaxDb);
            RightLevelDb = Clamp(rightDb, MinDb, MaxDb);
        }

        private void UpdatePeakDecay()
        {
            bool changed = false;
            var now = DateTime.UtcNow;
            double decayStep = PeakHoldDecayDbPerSecond * _peakTimer.Interval.TotalSeconds;

            if (LeftLevelDb >= _leftPeakDb)
            {
                _leftPeakDb = LeftLevelDb;
                _leftPeakTime = now;
                changed = true;
            }
            else if ((now - _leftPeakTime).TotalMilliseconds > PeakHoldTimeMs)
            {
                var newVal = Math.Max(MinDb, _leftPeakDb - decayStep);
                if (Math.Abs(newVal - _leftPeakDb) > 0.001)
                {
                    _leftPeakDb = newVal;
                    changed = true;
                }
            }

            if (RightLevelDb >= _rightPeakDb)
            {
                _rightPeakDb = RightLevelDb;
                _rightPeakTime = now;
                changed = true;
            }
            else if ((now - _rightPeakTime).TotalMilliseconds > PeakHoldTimeMs)
            {
                var newVal = Math.Max(MinDb, _rightPeakDb - decayStep);
                if (Math.Abs(newVal - _rightPeakDb) > 0.001)
                {
                    _rightPeakDb = newVal;
                    changed = true;
                }
            }

            if (changed && ShowPeakHold)
                InvalidateVisual();
        }

        // ---------- Layout ----------

        protected override Size MeasureOverride(Size availableSize)
        {
            double defaultWidth = Orientation == VuOrientation.Vertical
                ? 90
                : (double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width);

            double defaultHeight = Orientation == VuOrientation.Vertical
                ? (double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height)
                : 70;

            return new Size(
                double.IsInfinity(availableSize.Width) ? defaultWidth : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? defaultHeight : availableSize.Height);
        }

        // ---------- Render ----------

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 1 || bounds.Height <= 1)
                return;

            if (DisplayMode == VuDisplayMode.Analog)
            {
                RenderAnalog(context, bounds);
                return;
            }

            var gradient = BuildGradient();

            if (Orientation == VuOrientation.Vertical)
                RenderVertical(context, bounds, gradient);
            else
                RenderHorizontal(context, bounds, gradient);
        }

        private LinearGradientBrush BuildGradient()
        {
            double range = Math.Max(0.0001, MaxDb - MinDb);
            double warnFrac = Clamp((WarningDb - MinDb) / range, 0, 1);
            double dangerFrac = Clamp((DangerDb - MinDb) / range, 0, 1);

            var low = BrushColor(LowColor, Colors.LimeGreen);
            var mid = BrushColor(MidColor, Colors.Gold);
            var high = BrushColor(HighColor, Colors.Red);

            var stops = new GradientStops
            {
                new GradientStop(low, 0),
                new GradientStop(low, warnFrac),
                new GradientStop(mid, warnFrac),
                new GradientStop(mid, dangerFrac),
                new GradientStop(high, dangerFrac),
                new GradientStop(high, 1)
            };

            bool vertical = Orientation == VuOrientation.Vertical;

            return new LinearGradientBrush
            {
                GradientStops = stops,
                StartPoint = vertical
                    ? new RelativePoint(0, 1, RelativeUnit.Relative)
                    : new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = vertical
                    ? new RelativePoint(0, 0, RelativeUnit.Relative)
                    : new RelativePoint(1, 0, RelativeUnit.Relative)
            };
        }

        private void RenderVertical(DrawingContext ctx, Rect bounds, LinearGradientBrush gradient)
        {
            double textWidth = ShowDbText ? Math.Max(36, LabelFontSize * 3.4) : 0;
            double gap = ShowDbText ? 4 : 0;

            double reserved = (textWidth + gap) * 2;
            double barsWidth = BarThickness * 2 + BarSpacing;
            double remaining = Math.Max(0, bounds.Width - reserved);
            double barsAreaX = bounds.X + textWidth + gap + Math.Max(0, (remaining - barsWidth) / 2);

            var barLRect = new Rect(barsAreaX, bounds.Y, BarThickness, bounds.Height);
            var barRRect = new Rect(barLRect.Right + BarSpacing, bounds.Y, BarThickness, bounds.Height);

            RenderBar(ctx, barLRect, LeftLevelDb, _leftPeakDb, gradient, vertical: true);
            RenderBar(ctx, barRRect, RightLevelDb, _rightPeakDb, gradient, vertical: true);

            if (ShowDbText)
            {
                var leftText = CreateFormattedText(FormatDb(LeftLevelDb));
                var rightText = CreateFormattedText(FormatDb(RightLevelDb));

                var leftOrigin = new Point(
                    bounds.X + (textWidth - leftText.Width) / 2,
                    bounds.Center.Y - leftText.Height / 2);

                var rightOrigin = new Point(
                    barRRect.Right + gap + (textWidth - rightText.Width) / 2,
                    bounds.Center.Y - rightText.Height / 2);

                ctx.DrawText(leftText, leftOrigin);
                ctx.DrawText(rightText, rightOrigin);
            }
        }

        private void RenderHorizontal(DrawingContext ctx, Rect bounds, LinearGradientBrush gradient)
        {
            double textWidth = ShowDbText ? Math.Max(44, LabelFontSize * 4.2) : 0;
            double gap = ShowDbText ? 6 : 0;

            double barWidth = Math.Max(10, bounds.Width - textWidth - gap);
            double barsHeight = BarThickness * 2 + BarSpacing;
            double startY = bounds.Y + Math.Max(0, (bounds.Height - barsHeight) / 2);

            var barLRect = new Rect(bounds.X, startY, barWidth, BarThickness);
            var barRRect = new Rect(bounds.X, barLRect.Bottom + BarSpacing, barWidth, BarThickness);

            RenderBar(ctx, barLRect, LeftLevelDb, _leftPeakDb, gradient, vertical: false);
            RenderBar(ctx, barRRect, RightLevelDb, _rightPeakDb, gradient, vertical: false);

            if (ShowDbText)
            {
                var leftText = CreateFormattedText(FormatDb(LeftLevelDb));
                var rightText = CreateFormattedText(FormatDb(RightLevelDb));

                var leftOrigin = new Point(barLRect.Right + gap, barLRect.Center.Y - leftText.Height / 2);
                var rightOrigin = new Point(barRRect.Right + gap, barRRect.Center.Y - rightText.Height / 2);

                ctx.DrawText(leftText, leftOrigin);
                ctx.DrawText(rightText, rightOrigin);
            }
        }

        private void RenderBar(DrawingContext ctx, Rect barRect, double levelDb, double peakDb,
            LinearGradientBrush gradient, bool vertical)
        {
            if (DisplayMode == VuDisplayMode.Segmented)
                RenderBarSegmented(ctx, barRect, levelDb, peakDb, vertical);
            else
                RenderBarSolid(ctx, barRect, levelDb, peakDb, gradient, vertical);
        }

        private void RenderBarSolid(DrawingContext ctx, Rect barRect, double levelDb, double peakDb,
            LinearGradientBrush gradient, bool vertical)
        {
            var rrect = new RoundedRect(barRect, CornerRadius, CornerRadius);

            // Fondo (track) + degrade completo (fijo, siempre igual)
            ctx.DrawRectangle(TrackColor, null, rrect);
            ctx.DrawRectangle(gradient, null, rrect);

            // Mascara: tapa con TrackColor la parte que todavia no esta "encendida"
            double frac = Clamp((levelDb - MinDb) / Math.Max(0.0001, MaxDb - MinDb), 0, 1);

            Rect maskRect;
            if (vertical)
            {
                double litHeight = barRect.Height * frac;
                maskRect = new Rect(barRect.X, barRect.Y, barRect.Width, barRect.Height - litHeight);
            }
            else
            {
                double litWidth = barRect.Width * frac;
                maskRect = new Rect(barRect.X + litWidth, barRect.Y, barRect.Width - litWidth, barRect.Height);
            }

            if (maskRect.Width > 0.5 && maskRect.Height > 0.5)
                ctx.DrawRectangle(TrackColor, null, maskRect);

            // Peak hold
            if (ShowPeakHold)
            {
                double peakFrac = Clamp((peakDb - MinDb) / Math.Max(0.0001, MaxDb - MinDb), 0, 1);
                var pen = new Pen(PeakColor, 2);

                if (vertical)
                {
                    double y = barRect.Bottom - barRect.Height * peakFrac;
                    ctx.DrawLine(pen, new Point(barRect.X, y), new Point(barRect.Right, y));
                }
                else
                {
                    double x = barRect.X + barRect.Width * peakFrac;
                    ctx.DrawLine(pen, new Point(x, barRect.Y), new Point(x, barRect.Bottom));
                }
            }
        }

        /// <summary>Barra en segmentos discretos (estilo VU de hardware), con un LED de pico separado.</summary>
        private void RenderBarSegmented(DrawingContext ctx, Rect barRect, double levelDb, double peakDb, bool vertical)
        {
            var rrect = new RoundedRect(barRect, CornerRadius, CornerRadius);
            ctx.DrawRectangle(TrackColor, null, rrect);

            double range = Math.Max(0.0001, MaxDb - MinDb);
            double warnFrac = Clamp((WarningDb - MinDb) / range, 0, 1);
            double dangerFrac = Clamp((DangerDb - MinDb) / range, 0, 1);
            double levelFrac = Clamp((levelDb - MinDb) / range, 0, 1);
            double peakFrac = Clamp((peakDb - MinDb) / range, 0, 1);

            var low = BrushColor(LowColor, Colors.LimeGreen);
            var mid = BrushColor(MidColor, Colors.Gold);
            var high = BrushColor(HighColor, Colors.Red);

            int segCount = Math.Max(1, SegmentCount);
            double gap = Math.Max(0, SegmentGap);
            double totalGap = gap * (segCount - 1);
            double segSize = vertical
                ? Math.Max(0, (barRect.Height - totalGap) / segCount)
                : Math.Max(0, (barRect.Width - totalGap) / segCount);

            for (var i = 0; i < segCount; i++)
            {
                double segCenterFrac = (i + 0.5) / segCount;

                var zoneColor = segCenterFrac >= dangerFrac ? high
                    : segCenterFrac >= warnFrac ? mid
                    : low;

                bool isPeakSeg = ShowPeakHold && peakFrac > levelFrac + (0.5 / segCount)
                    && Math.Abs(segCenterFrac - peakFrac) <= (0.5 / segCount);
                bool lit = segCenterFrac <= levelFrac;

                IBrush segBrush = isPeakSeg ? PeakColor
                    : lit ? new SolidColorBrush(zoneColor)
                    : new SolidColorBrush(DimColor(zoneColor));

                Rect segRect;
                if (vertical)
                {
                    double y = barRect.Bottom - (i + 1) * segSize - i * gap;
                    segRect = new Rect(barRect.X, y, barRect.Width, segSize);
                }
                else
                {
                    double x = barRect.X + i * (segSize + gap);
                    segRect = new Rect(x, barRect.Y, segSize, barRect.Height);
                }

                ctx.DrawRectangle(segBrush, null, segRect);
            }
        }

        private FormattedText CreateFormattedText(string text)
        {
            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                LabelFontSize,
                LabelColor);
        }

        private static string FormatDb(double db) => db <= -99 ? "-inf" : $"{db:0.0} dB";

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private static Color BrushColor(IBrush brush, Color fallback) =>
            brush is ISolidColorBrush s ? s.Color : fallback;

        /// <summary>Versión oscurecida de un color, para los segmentos "apagados" del modo Segmentado.</summary>
        private static Color DimColor(Color c, double factor = 0.16) =>
            Color.FromArgb(255, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

        // ---------- Modo Analógico (aguja) ----------

        private void RenderAnalog(DrawingContext ctx, Rect bounds)
        {
            const double gap = 10;
            double gaugeWidth = (bounds.Width - gap) / 2;

            var leftRect = new Rect(bounds.X, bounds.Y, gaugeWidth, bounds.Height);
            var rightRect = new Rect(bounds.X + gaugeWidth + gap, bounds.Y, gaugeWidth, bounds.Height);

            RenderNeedleGauge(ctx, leftRect, LeftLevelDb, "L");
            RenderNeedleGauge(ctx, rightRect, RightLevelDb, "R");
        }

        private void RenderNeedleGauge(DrawingContext ctx, Rect rect, double levelDb, string label)
        {
            var bg = new RoundedRect(rect, CornerRadius, CornerRadius);
            ctx.DrawRectangle(TrackColor, null, bg);

            double textReserve = ShowDbText ? LabelFontSize + 8 : 0;
            double cx = rect.Center.X;
            double cy = rect.Bottom - textReserve - rect.Height * 0.08;
            double radius = Math.Max(4, Math.Min(rect.Width * 0.46, (rect.Height - textReserve) * 0.85));

            const double startDeg = -55, endDeg = 55;

            // Arco de fondo
            DrawArc(ctx, cx, cy, radius, startDeg, endDeg, new Pen(LabelColor, 1.5));

            // Marcas en Warning y Danger
            DrawTick(ctx, cx, cy, radius, AngleForDb(WarningDb, startDeg, endDeg), BrushColor(MidColor, Colors.Gold));
            DrawTick(ctx, cx, cy, radius, AngleForDb(DangerDb, startDeg, endDeg), BrushColor(HighColor, Colors.Red));

            // Aguja
            var needleAngle = AngleForDb(levelDb, startDeg, endDeg);
            var needleEnd = PointOnArc(cx, cy, radius * 0.9, needleAngle);
            ctx.DrawLine(new Pen(PeakColor, 2), new Point(cx, cy), needleEnd);
            ctx.DrawEllipse(PeakColor, null, new Point(cx, cy), 3, 3);

            // Peak hold: una marca fija en el punto más alto alcanzado
            if (ShowPeakHold)
            {
                var peakDb = label == "L" ? _leftPeakDb : _rightPeakDb;
                var peakAngle = AngleForDb(peakDb, startDeg, endDeg);
                DrawTick(ctx, cx, cy, radius, peakAngle, BrushColor(PeakColor, Colors.White));
            }

            if (ShowDbText)
            {
                var text = CreateFormattedText($"{label}  {FormatDb(levelDb)}");
                ctx.DrawText(text, new Point(rect.Center.X - text.Width / 2, rect.Bottom - text.Height));
            }
        }

        private double AngleForDb(double db, double startDeg, double endDeg)
        {
            double frac = Clamp((db - MinDb) / Math.Max(0.0001, MaxDb - MinDb), 0, 1);
            return startDeg + frac * (endDeg - startDeg);
        }

        /// <summary>Punto sobre un arco centrado en (cx,cy): angulo 0 = arriba (12 en punto), positivo = sentido horario.</summary>
        private static Point PointOnArc(double cx, double cy, double radius, double angleDeg)
        {
            var rad = angleDeg * Math.PI / 180.0;
            return new Point(cx + radius * Math.Sin(rad), cy - radius * Math.Cos(rad));
        }

        private static void DrawArc(DrawingContext ctx, double cx, double cy, double radius,
            double startDeg, double endDeg, IPen pen)
        {
            var start = PointOnArc(cx, cy, radius, startDeg);
            var end = PointOnArc(cx, cy, radius, endDeg);
            var isLargeArc = Math.Abs(endDeg - startDeg) > 180;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(start, false);
                gc.ArcTo(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }

        private static void DrawTick(DrawingContext ctx, double cx, double cy, double radius, double angleDeg, Color color)
        {
            var inner = PointOnArc(cx, cy, radius - 6, angleDeg);
            var outer = PointOnArc(cx, cy, radius + 3, angleDeg);
            ctx.DrawLine(new Pen(new SolidColorBrush(color), 2), inner, outer);
        }
    }
}
