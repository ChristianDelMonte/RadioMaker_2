using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RMVoice.UI.Localization;
using AvaloniaShapePath = Avalonia.Controls.Shapes.Path;

namespace RMVoice.UI.Views;

public partial class MainWindow : RMVoice.UI.Controls.CustomChromeWindow
{
    private AvaloniaShapePath? _maximizeIcon;

    public MainWindow()
    {
        InitializeComponent();
        InitializeCustomChrome();
        this.FindControl<ComboBox>("LanguageSelector")!.SelectedIndex =
            VoiceLocalizationService.CurrentLanguage == "en" ? 1 : 0;
    }

    private void LanguageSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox selector || selector.SelectedIndex < 0) return;
        VoiceLocalizationService.SetLanguage(selector.SelectedIndex == 1 ? "en" : "es");
    }

    private void InitializeCustomChrome()
    {
        var minimize = this.FindControl<Button>("MinimizeButton");
        if (minimize is not null)
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var maximize = this.FindControl<Button>("MaximizeButton");
        if (maximize is not null)
        {
            _maximizeIcon = maximize.Content as AvaloniaShapePath;
            maximize.Click += (_, _) => ToggleMaximize();
        }

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
            close.Click += (_, _) => Close();

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                UpdateMaximizeIcon();
        };

        var titleBar = this.FindControl<Border>("CustomTitleBar");
        if (titleBar is not null)
            WireTitleBarDrag(titleBar);
    }

    private void WireTitleBarDrag(Border titleBar)
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            var source = e.Source as Control;
            if (source is not null)
            {
                var parent = source.Parent;
                while (parent is not null)
                {
                    if (parent is Button) return;
                    parent = (parent as Control)?.Parent;
                }
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                e.Handled = true;
            }
        };
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeIcon()
    {
        if (_maximizeIcon is null) return;

        _maximizeIcon.Data = WindowState == WindowState.Maximized
            ? Geometry.Parse("M 4 2 L 12 2 L 12 10 M 2 4 L 2 12 L 10 12")  // restore
            : Geometry.Parse("M 2 2 L 12 2 L 12 12 L 2 12 Z");            // maximize
    }
}
