namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Zona de la ventana donde vive un TabHost. Determina:
///   - Que pestaña fija inicial tiene (Eventos / Cart / ninguna).
///   - Que tipos de pestaña ofrece el boton "+".
/// </summary>
public enum TabZone
{
    Left = 0,
    Center = 1,
    Right = 2
}