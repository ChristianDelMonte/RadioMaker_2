using Avalonia.Controls;
using Avalonia.Interactivity;
using RadioMaker.UI.Controls;

namespace RadioMaker.UI.Views;

public partial class AddFavoriteDialog : CustomChromeWindow
{
    public AddFavoriteDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}