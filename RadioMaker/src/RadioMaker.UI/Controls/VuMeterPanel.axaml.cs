using Avalonia.Controls;
using RadioMaker.UI.ViewModels;
using Avalonia;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Panel de configuracion del VU meter (integrado en la ventana unificada
/// de configuracion).
///
/// El <see cref="VuMeterSettingsWindowViewModel"/> tiene un timer interno
/// que genera la señal de demo para el preview en vivo. Para no consumir
/// CPU cuando el usuario esta en otra seccion de la ventana, arrancamos el
/// timer SOLO cuando el panel entra al arbol visual, y lo paramos cuando
/// sale (cambio de seccion o cierre de la ventana).
/// </summary>
public partial class VuMeterPanel : UserControl
{
    public VuMeterPanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is VuMeterSettingsWindowViewModel vm)
            vm.StartTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is VuMeterSettingsWindowViewModel vm)
            vm.StopTimer();
    }
}
