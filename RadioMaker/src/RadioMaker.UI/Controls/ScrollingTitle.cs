using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RadioMaker.UI.Controls;

/// <summary>Scrolls a single line of text when it is wider than the available space.</summary>
public sealed class ScrollingTitle : Decorator
{
    private const double PixelsPerSecond = 26;
    private const double StartPauseSeconds = 1.5;
    private const double EndPauseSeconds = 1.5;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };
    private readonly Stopwatch _cycle = new();
    private readonly TranslateTransform _translation = new();
    private Control? _title;
    private bool _attached;
    private double _overflow;

    public ScrollingTitle()
    {
        ClipToBounds = true;
        _timer.Tick += OnTick;
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            Restart();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            Restart();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ChildProperty) return;

        if (_title is not null)
        {
            _title.PropertyChanged -= OnTitlePropertyChanged;
            _title.RenderTransform = null;
        }

        _title = Child;
        if (_title is not null)
        {
            _title.RenderTransform = _translation;
            _title.PropertyChanged += OnTitlePropertyChanged;
        }

        ResetForNewText();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_title is null) return default;

        _title.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        return new Size(Math.Min(_title.DesiredSize.Width, availableSize.Width), _title.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_title is null) return finalSize;

        var textWidth = _title.DesiredSize.Width;
        _title.Arrange(new Rect(0, 0, Math.Max(textWidth, finalSize.Width), finalSize.Height));

        var overflow = finalSize.Width > 0 ? Math.Max(0, textWidth - finalSize.Width) : 0;
        if (_overflow != overflow)
        {
            _overflow = overflow;
            Restart();
        }

        return finalSize;
    }

    private void OnTitlePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBlock.TextProperty)
            ResetForNewText();
    }

    private void ResetForNewText()
    {
        _overflow = 0;
        Restart();
        InvalidateMeasure();
    }

    private void Restart()
    {
        _translation.X = 0;
        if (_attached && _overflow > 0.5)
        {
            _cycle.Restart();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _cycle.Reset();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var movingSeconds = _cycle.Elapsed.TotalSeconds - StartPauseSeconds;
        if (movingSeconds < 0) return;

        if (movingSeconds >= _overflow / PixelsPerSecond + EndPauseSeconds)
        {
            Restart();
            return;
        }

        _translation.X = -Math.Min(_overflow, movingSeconds * PixelsPerSecond);
    }
}
