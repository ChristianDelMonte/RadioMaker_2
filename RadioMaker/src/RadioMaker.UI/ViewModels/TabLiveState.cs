namespace RadioMaker.UI.ViewModels;

/// -------------
/// Estado de "al aire" de una pestaña, usado para el indicador visual
/// (circulo rojo / amarillo al lado del titulo).
/// -------------
public enum TabLiveState
{
    /// -------------Sin indicador visible.</summary>
    None = 0,

    /// -------------La pestaña esta al aire (reproduciendo en el bus del scheduler).</summary>
    Live = 1,

    /// -------------La pestaña fue pausada por un item ALTA del scheduler.</summary>
    PausedByScheduler = 2
}