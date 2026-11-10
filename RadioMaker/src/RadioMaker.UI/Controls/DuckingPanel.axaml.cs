using Avalonia;
using Avalonia.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Controls;

public partial class DuckingPanel : UserControl
{
    public DuckingPanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is DuckingPanelViewModel vm)
            vm.StartTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is DuckingPanelViewModel vm)
            vm.StopTimer();
    }
}