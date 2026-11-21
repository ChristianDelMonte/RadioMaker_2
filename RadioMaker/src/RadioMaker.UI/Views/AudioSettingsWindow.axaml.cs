using Avalonia.Controls;
using Avalonia.Interactivity;
using RadioMaker.Core.Configuration;
using RadioMaker.UI.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind del panel de configuracion unificado.
///
/// Hereda de CustomChromeWindow para tener la barra de titulo personalizada
/// en Windows/macOS y la nativa en Linux.
/// -------------
public partial class AudioSettingsWindow : CustomChromeWindow
{
    public AudioSettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Persistir la configuracion de buses al cerrar.
        if (DataContext is AudioSettingsWindowViewModel vm)
        {
            try
            {
                var configs = vm.BusesPanel.ToConfigs();
                BusConfigurationStore.Save(configs);
            }
            catch { /* best effort */ }
        }

        base.OnClosing(e);
    }
}