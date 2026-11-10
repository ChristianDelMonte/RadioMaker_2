namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Estado de "al aire" de una pestaña, usado para el indicador visual
/// (circulo rojo / amarillo al lado del titulo).
/// </summary>
public enum TabLiveState
{
    /// <summary>Sin indicador visible.</summary>
    None = 0,

    /// <summary>La pestaña esta al aire (reproduciendo en el bus del scheduler).</summary>
    Live = 1,

    /// <summary>La pestaña fue pausada por un item ALTA del scheduler.</summary>
    PausedByScheduler = 2
}