using Avalonia.Controls;
using RadioMaker.UI.ViewModels;
using Avalonia;
using Avalonia.VisualTree;

namespace RadioMaker.UI.Controls;

/// -------------
/// Panel de configuracion del VU meter (integrado en la ventana unificada
/// de configuracion).
///
/// El <see cref="VuMeterSettingsWindowViewModel"/> tiene un timer interno
/// que genera la señal de demo para el preview en vivo. Para no consumir
/// CPU cuando el usuario esta en otra seccion de la ventana, arrancamos el
/// timer SOLO cuando el panel entra al arbol visual, y lo paramos cuando
/// sale (cambio de seccion o cierre de la ventana).
/// -------------
public partial class VuMeterPanel : UserControl
{
    private VuMeterSettingsWindowViewModel? _previewViewModel;

    public VuMeterPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (this.IsAttachedToVisualTree())
                SetPreviewViewModel(DataContext as VuMeterSettingsWindowViewModel);
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        SetPreviewViewModel(DataContext as VuMeterSettingsWindowViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        SetPreviewViewModel(null);
    }

    private void SetPreviewViewModel(VuMeterSettingsWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_previewViewModel, viewModel)) return;

        _previewViewModel?.StopTimer();
        _previewViewModel = viewModel;
        _previewViewModel?.StartTimer();
    }
}
