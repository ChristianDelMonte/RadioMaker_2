using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Modos de visualizacion del analizador de espectro.
/// </summary>
public enum SpectrumMode
{
    /// <summary>FFT normal: picos de frecuencia con interpolacion suave.</summary>
    Fft = 0,

    /// <summary>FFT logaritmico: 28 bandas agrupadas estilo ecualizador.</summary>
    LogBands = 1,

    /// <summary>3D waterfall: mapa de calor desplazandose horizontalmente.</summary>
    Waterfall = 2,

    /// <summary>Osciloscopio: forma de onda en el dominio del tiempo.</summary>
    Waveform = 3
}

/// <summary>
/// Control de analisis de espectro de audio.
///
/// Lee datos del canal bindeado (~30 veces por segundo) y los dibuja sobre
/// el DrawingContext con uno de los 4 modos disponibles. Hacer click sobre
/// el control cambia entre modos.
///
/// Basado en el ejemplo oficial "spectrum.c" de la libreria BASS de Un4seen.
/// </summary>
public sealed class SpectrumAnalyzer : Control
{
    // ───── Propiedades bindables ─────────────────────────────────────────

    public static readonly StyledProperty<IAudioChannel?> ChannelProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, IAudioChannel?>(nameof(Channel));

    public static readonly StyledProperty<IAudioSpectrum?> SpectrumProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, IAudioSpectrum?>(nameof(Spectrum));

    public static readonly StyledProperty<SpectrumMode> ModeProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, SpectrumMode>(nameof(Mode));

    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, bool>(nameof(IsRunning), true);

    public IAudioChannel? Channel
    {
        get => GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public IAudioSpectrum? Spectrum
    {
        get => GetValue(SpectrumProperty);
        set => SetValue(SpectrumProperty, value);
    }

    public SpectrumMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    // ───── Estado interno ────────────────────────────────────────────────

    private DispatcherTimer? _timer;
    private float[] _fftBuffer = new float[1024];
    private float[] _waveBuffer = new float[8192];

    /// <summary>Buffer circular para el modo waterfall (una columna por tick).</summary>
    private byte[,]? _waterfallBuffer;
    private int _waterfallWidth;
    private int _waterfallHeight;
    private int _waterfallPos;

    // Numero de bandas para el modo LogBands (identico al ejemplo C).
    private const int LogBands = 28;

    // Colores de la paleta.
    private static readonly Color BgColor = Color.Parse("#0A0A0C");
    private static readonly Color GridColor = Color.Parse("#1E2230");
    private static readonly Color GreenColor = Color.Parse("#10B981");
    private static readonly Color AmberColor = Color.Parse("#F59E0B");
    private static readonly Color RedColor = Color.Parse("#EF4444");
    private static readonly Color AccentColor = Color.Parse("#4D6BFE");

    // ───── Constructor ───────────────────────────────────────────────────

    public SpectrumAnalyzer()
    {
        ClipToBounds = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 FPS
        _timer.Tick += (_, _) => OnTimerTick();

        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    // ───── API publica ───────────────────────────────────────────────────

    public void Start()
    {
        if (IsRunning && _timer is { IsEnabled: false })
            _timer.Start();
    }

    public void Stop()
    {
        if (_timer is { IsEnabled: true })
            _timer.Stop();
    }

    /// <summary>Limpia los buffers (por ejemplo al cambiar de canal).</summary>
    public void Reset()
    {
        Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
        Array.Clear(_waveBuffer, 0, _waveBuffer.Length);

        if (_waterfallBuffer is not null)
            Array.Clear(_waterfallBuffer, 0, _waterfallBuffer.Length);

        _waterfallPos = 0;
        InvalidateVisual();
    }

    // ───── Ciclo de actualizacion ────────────────────────────────────────

    private void OnTimerTick()
    {
        if (!IsRunning) return;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChannelProperty)
        {
            Reset();

            // Reconectar timer si cambio el canal.
            if (Channel is not null)
                Start();
        }
        else if (change.Property == SpectrumProperty)
        {
            Reset();
            if (Spectrum is not null) Start();
        }
        else if (change.Property == IsRunningProperty)
        {
            if (IsRunning) Start();
            else Stop();
        }
        else if (change.Property == ModeProperty)
        {
            // Reiniciar waterfall al cambiar de modo.
            Reset();
        }
    }

    // ───── Interaccion ───────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Cambiar de modo (como el ejemplo C).
        int next = ((int)Mode + 1) % 4;
        Mode = (SpectrumMode)next;

        e.Handled = true;
    }

    // ───── Render ────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 20 || bounds.Height < 20) return;

        // Fondo.
        ctx.FillRectangle(new SolidColorBrush(BgColor), bounds);

        var spectrum = Spectrum ?? Channel?.Spectrum;
        if (spectrum is null)
        {
            DrawCenteredText(ctx, bounds, "Sin canal activo");
            return;
        }

        // Dibujar segun el modo.
        switch (Mode)
        {
            case SpectrumMode.Fft:
                DrawFft(ctx, bounds, spectrum);
                break;

            case SpectrumMode.LogBands:
                DrawLogBands(ctx, bounds, spectrum);
                break;

            case SpectrumMode.Waterfall:
                DrawWaterfall(ctx, bounds, spectrum);
                break;

            case SpectrumMode.Waveform:
                DrawWaveform(ctx, bounds, spectrum);
                break;
        }

        // Etiqueta del modo en la esquina inferior izquierda.
        DrawModeLabel(ctx, bounds);
    }

    // ───── Modo 1: FFT normal ────────────────────────────────────────────

    private void DrawFft(DrawingContext ctx, Rect bounds, IAudioSpectrum spectrum)
    {
        if (!spectrum.ReadFft(_fftBuffer)) return;

        int displayWidth = Math.Max(1, (int)bounds.Width);
        int displayHeight = Math.Max(1, (int)bounds.Height);

        int bins = Math.Min(displayWidth / 2, spectrum.FftBinCount);

        // Dibuja barras interpoladas del ejemplo C: se calcula la altura
        // y=sqrt(fft[i])*3*height, se cap-ea, y se interpola entre bins
        // consecutivos para suavizar la curva.

        double prevY = 0;
        bool hasPrev = false;

        var brush = CreateGradientBrush(bounds);

        for (int x = 0; x < bins; x++)
        {
            // Tomar el bin x (offset +1 porque el 0 es DC).
            double mag = x + 1 < _fftBuffer.Length ? _fftBuffer[x + 1] : 0;

            // Escalar con sqrt para dar visibilidad a valores bajos (como el ejemplo C).
            double y = Math.Sqrt(Math.Max(0, mag)) * 3.0 * displayHeight - 4;
            if (y > displayHeight) y = displayHeight;
            if (y < 0) y = 0;

            double barX = x * (bounds.Width / bins);
            double barW = (bounds.Width / bins) + 1; // +1 para evitar gaps

            // Dibujar barra vertical.
            var barRect = new Rect(barX, bounds.Height - y, barW, y);
            ctx.FillRectangle(brush, barRect);

            // Interpolar del bin anterior (linea horizontal arriba).
            if (hasPrev && Math.Abs(prevY - y) > 0.5)
            {
                double minY = Math.Min(prevY, y);
                double maxY = Math.Max(prevY, y);
                var interpRect = new Rect(
                    barX - barW,
                    bounds.Height - maxY,
                    barW * 2,
                    maxY - minY);
                // No dibujamos la interpolacion como rectangulo — el ejemplo C
                // dibuja una linea de pixel por pixel. Con barW ya solapado,
                // no hace falta interpolacion adicional.
            }

            prevY = y;
            hasPrev = true;
        }

        // Grid horizontal de referencia (-6 / -24 / -48 dB aprox).
        DrawHorizontalGrid(ctx, bounds);
    }

    // ───── Modo 2: FFT logaritmico (28 bandas) ───────────────────────────

    private void DrawLogBands(DrawingContext ctx, Rect bounds, IAudioSpectrum spectrum)
    {
        if (!spectrum.ReadFft(_fftBuffer)) return;

        int displayHeight = Math.Max(1, (int)bounds.Height);

        double bandWidth = bounds.Width / LogBands;

        int b0 = 0;
        var brush = CreateGradientBrush(bounds);

        for (int x = 0; x < LogBands; x++)
        {
            // Calcular rango de bins para esta banda (exactamente como el
            // ejemplo C: b1 = 2^(x*10/(BANDS-1))).
            int b1 = (int)Math.Pow(2, x * 10.0 / (LogBands - 1));
            if (b1 <= b0) b1 = b0 + 1;
            if (b1 > spectrum.FftBinCount - 1) b1 = spectrum.FftBinCount - 1;

            // Peak entre los bins de esta banda.
            float peak = 0;
            for (; b0 < b1; b0++)
            {
                int idx = b0 + 1; // offset por DC
                if (idx < _fftBuffer.Length && _fftBuffer[idx] > peak)
                    peak = _fftBuffer[idx];
            }

            double y = Math.Sqrt(Math.Max(0, peak)) * 3.0 * displayHeight - 4;
            if (y > displayHeight) y = displayHeight;
            if (y < 0) y = 0;

            double barX = x * bandWidth;
            double barW = bandWidth - 2;

            var barRect = new Rect(barX, bounds.Height - y, barW, y);
            ctx.FillRectangle(brush, barRect);
        }

        DrawHorizontalGrid(ctx, bounds);
    }

    // ───── Modo 3: Waterfall ─────────────────────────────────────────────

    private void DrawWaterfall(DrawingContext ctx, Rect bounds, IAudioSpectrum spectrum)
    {
        if (!spectrum.ReadFft(_fftBuffer)) return;

        int w = Math.Max(1, (int)bounds.Width);
        int h = Math.Max(1, (int)bounds.Height);

        // Reasignar buffer si cambio el tamano.
        if (_waterfallBuffer is null || _waterfallWidth != w || _waterfallHeight != h)
        {
            _waterfallBuffer = new byte[w, h];
            _waterfallWidth = w;
            _waterfallHeight = h;
            _waterfallPos = 0;
        }

        // Escribir la columna actual.
        int binsToDraw = Math.Min(h, spectrum.FftBinCount);
        for (int y = 0; y < binsToDraw; y++)
        {
            int binIdx = y + 1;
            float mag = binIdx < _fftBuffer.Length ? _fftBuffer[binIdx] : 0;

            double v = Math.Sqrt(Math.Max(0, mag)) * 3.0 * 255;
            if (v > 255) v = 255;
            if (v < 0) v = 0;

            // Guardamos el byte en la fila (h-1-y) para que las bajas frecuencias
            // queden abajo (coordenada 0 = top).
            _waterfallBuffer[_waterfallPos, h - 1 - y] = (byte)v;
        }

        // Render: escribir la matriz en un WriteableBitmap.
        // Por simplicidad dibujamos rect por rect (funciona a 30 FPS en 400px).
        var bitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var ptr = (byte*)fb.Address;
                int stride = fb.RowBytes;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // Columna logica = (x + waterfallPos) % w
                        int col = (x + _waterfallPos) % w;
                        byte intensity = _waterfallBuffer[col, y];

                        var color = PaletteColor(intensity);

                        int offset = y * stride + x * 4;
                        ptr[offset + 0] = color.B; // B
                        ptr[offset + 1] = color.G; // G
                        ptr[offset + 2] = color.R; // R
                        ptr[offset + 3] = 255;     // A
                    }
                }
            }
        }

        ctx.DrawImage(bitmap, bounds);

        // Avanzar la posicion del waterfall.
        _waterfallPos = (_waterfallPos + 1) % w;
    }

    // ───── Modo 4: Waveform ──────────────────────────────────────────────

    private void DrawWaveform(DrawingContext ctx, Rect bounds, IAudioSpectrum spectrum)
    {
        int width = Math.Max(1, (int)bounds.Width);
        int channels = Math.Max(1, spectrum.ChannelCount);

        // Leer muestras.
        int frames = width;
        int needed = frames * channels;

        if (_waveBuffer.Length < needed)
            _waveBuffer = new float[needed];

        if (!spectrum.ReadWaveform(_waveBuffer, frames)) return;

        // Para cada canal, dibujar una linea conectando las muestras.
        // En estereo, el canal izquierdo se dibuja con un tono y el derecho con otro.
        for (int c = 0; c < channels; c++)
        {
            var color = channels == 1
                ? GreenColor
                : (c == 0 ? GreenColor : AmberColor);

            var pen = new Pen(new SolidColorBrush(color), 1.5);

            double prevX = 0;
            double prevY = bounds.Height / 2;

            for (int x = 0; x < frames; x++)
            {
                float sample = _waveBuffer[x * channels + c];
                if (sample > 1) sample = 1;
                if (sample < -1) sample = -1;

                // Mapear -1..1 al rango [height-1 .. 0], con 0 en el centro.
                double y = bounds.Height / 2 - (sample * (bounds.Height / 2) * 0.9);

                double screenX = x * (bounds.Width / frames);

                if (x > 0)
                {
                    ctx.DrawLine(pen, new Point(prevX, prevY), new Point(screenX, y));
                }

                prevX = screenX;
                prevY = y;
            }
        }

        // Linea central (eje 0).
        var centerPen = new Pen(new SolidColorBrush(GridColor), 1);
        ctx.DrawLine(centerPen,
            new Point(0, bounds.Height / 2),
            new Point(bounds.Width, bounds.Height / 2));
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve un color de la paleta segun la intensidad (0..255).
    /// Usa una rampa suave: azul oscuro → azul → verde → ambar → rojo.
    /// </summary>
    private static Color PaletteColor(byte intensity)
    {
        if (intensity == 0) return BgColor;

        double t = intensity / 255.0;

        // Rampa de 5 colores.
        if (t < 0.15) return Interp(BgColor, AccentColor, t / 0.15);
        if (t < 0.4) return Interp(AccentColor, GreenColor, (t - 0.15) / 0.25);
        if (t < 0.7) return Interp(GreenColor, AmberColor, (t - 0.4) / 0.3);
        return Interp(AmberColor, RedColor, (t - 0.7) / 0.3);
    }

    private static Color Interp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static IBrush CreateGradientBrush(Rect bounds)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(GreenColor, 0.0),
                new GradientStop(GreenColor, 0.5),
                new GradientStop(AmberColor, 0.75),
                new GradientStop(RedColor, 1.0)
            }
        };
    }

    private static void DrawHorizontalGrid(DrawingContext ctx, Rect bounds)
    {
        var pen = new Pen(new SolidColorBrush(GridColor), 1);

        // 3 lineas horizontales a 25%, 50%, 75%.
        for (int i = 1; i <= 3; i++)
        {
            double y = bounds.Height * (i / 4.0);
            ctx.DrawLine(pen,
                new Point(0, y),
                new Point(bounds.Width, y));
        }
    }

    private void DrawModeLabel(DrawingContext ctx, Rect bounds)
    {
        string text = Mode switch
        {
            SpectrumMode.Fft => "FFT · click para cambiar",
            SpectrumMode.LogBands => "Bandas · click para cambiar",
            SpectrumMode.Waterfall => "Waterfall · click para cambiar",
            SpectrumMode.Waveform => "Onda · click para cambiar",
            _ => "Spectrum"
        };

        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            10,
            new SolidColorBrush(Colors.White, 0.35));

        ctx.DrawText(ft, new Point(8, bounds.Height - ft.Height - 6));
    }

    private static void DrawCenteredText(DrawingContext ctx, Rect bounds, string text)
    {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            12,
            new SolidColorBrush(Colors.White, 0.4));

        var pos = new Point(
            (bounds.Width - ft.Width) / 2,
            (bounds.Height - ft.Height) / 2);

        ctx.DrawText(ft, pos);
    }
}
