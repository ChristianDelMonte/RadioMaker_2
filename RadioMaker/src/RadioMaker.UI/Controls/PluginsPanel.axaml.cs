using Avalonia.Controls;

namespace RadioMaker.UI.Controls;

/// -------------
/// Panel de plugins de formato (integrado en la ventana unificada
/// de configuracion).
///
/// Es solo lectura: muestra el estado de los addons cargados en esta
/// sesion y los formatos soportados. No tiene logica propia.
/// -------------
public partial class PluginsPanel : UserControl
{
    public PluginsPanel()
    {
        InitializeComponent();
    }
}
