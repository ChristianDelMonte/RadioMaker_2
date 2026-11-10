using Avalonia.Controls;
using Avalonia.Interactivity;
using RadioMaker.Core.Configuration;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

public partial class AudioBusesWindow : Window
{
    public AudioBusesWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        // Persistir los cambios antes de cerrar.
        if (DataContext is AudioBusesWindowViewModel vm)
        {
            try
            {
                BusConfigurationStore.Save(vm.ToConfigs());
            }
            catch
            {
                // best effort — si falla el guardado no bloqueamos el cierre.
            }
        }

        Close();
    }
}
