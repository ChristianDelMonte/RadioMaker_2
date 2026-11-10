using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Panel compacto con dos lineas de display digital LED (7 segmentos).
///
/// El fondo del panel es theme-aware:
///   - Dark:  casi negro (#0A0A0C).
///   - Light: cyan claro (#D0EEF5), simulando un display LED "clasico"
///            iluminado.
///
/// Los colores de los segmentos los maneja <see cref="LedLine"/>, que
/// tambien es theme-aware.
/// </summary>
public partial class LedPanel : UserControl
{
    public static readonly StyledProperty<string> Line1TextProperty =
        AvaloniaProperty.Register<LedPanel, string>(nameof(Line1Text), "00:00");

    public static readonly StyledProperty<string> Line2TextProperty =
        AvaloniaProperty.Register<LedPanel, string>(nameof(Line2Text), "-00:00");

    public static readonly StyledProperty<IBrush?> OnBrushProperty =
        AvaloniaProperty.Register<LedPanel, IBrush?>(nameof(OnBrush));

    public static readonly StyledProperty<IBrush?> OffBrushProperty =
        AvaloniaProperty.Register<LedPanel, IBrush?>(nameof(OffBrush));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LedPanel, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<LedPanel, IBrush?>(nameof(InactiveBrush));

    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<LedPanel, double>(nameof(LineHeight), 22);

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

    public string Line1Text { get => GetValue(Line1TextProperty); set => SetValue(Line1TextProperty, value); }
    public string Line2Text { get => GetValue(Line2TextProperty); set => SetValue(Line2TextProperty, value); }
    public IBrush? OnBrush { get => GetValue(OnBrushProperty); set => SetValue(OnBrushProperty, value); }
    public IBrush? OffBrush { get => GetValue(OffBrushProperty); set => SetValue(OffBrushProperty, value); }
    public double LineHeight { get => GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }

    public LedPanel()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    // ───── Suscripcion al tema ───────────────────────────────────────────

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyThemeBackground();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ApplyThemeBackground);
    }

    /// <summary>
    /// Aplica el color de fondo del panel segun el tema actual.
    ///   - Dark:  casi negro.
    ///   - Light: cyan claro (display LED iluminado).
    /// </summary>
    private void ApplyThemeBackground()
    {
        var border = this.FindControl<Border>("PanelBorder");
        if (border is null) return;

        border.Background = new SolidColorBrush(
            ThemeService.IsDarkVariant
                ? Color.Parse("#0A0A0C")
                : Color.Parse("#D0EEF5"));
    }
}