using Avalonia.Controls;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Code-behind del panel del scheduler.
/// No tiene handlers propios: toda la interaccion se maneja via comandos
/// bindeados en el ViewModel.
/// </summary>
public partial class SchedulerPanel : UserControl
{
    public SchedulerPanel()
    {
        InitializeComponent();
    }
}