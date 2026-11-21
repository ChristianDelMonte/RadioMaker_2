namespace RadioMaker.UI.ViewModels;

/// -------------
/// Zona de la ventana donde vive un TabHost. Determina:
///   - Que pestaña fija inicial tiene (Eventos / Cart / ninguna).
///   - Que tipos de pestaña ofrece el boton "+".
/// -------------
public enum TabZone
{
    Left = 0,
    Center = 1,
    Right = 2
}