using Avalonia.Controls;
using Avalonia.Interactivity;
using RadioMaker.UI.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class AudioTagEditorWindow : CustomChromeWindow
{
    public AudioTagEditorWindow()
    {
        InitializeComponent();
    }

    private AudioTagEditorViewModel? GetVm() => DataContext as AudioTagEditorViewModel;

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) { Close(false); return; }

        if (SaveButton is not null) SaveButton.IsEnabled = false;
        if (CancelButton is not null) CancelButton.IsEnabled = false;

        try
        {
            var ok = await vm.SaveAsync();
            if (ok)
            {
                Close(true);
                return;
            }
        }
        finally
        {
            if (SaveButton is not null) SaveButton.IsEnabled = true;
            if (CancelButton is not null) CancelButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}