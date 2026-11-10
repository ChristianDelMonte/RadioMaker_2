using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Control reutilizable que muestra un boton compacto con el bus de salida
/// actual del modulo y un flyout para cambiarlo.
///
/// El DataContext debe ser un <see cref="ViewModels.ModuleBusSelectorViewModel"/>.
/// El boton se deshabilita solo cuando el modo "bus global" esta activo
/// (CanChangeBus = false), para dejar claro que no se puede cambiar
/// mientras todos comparten salida.
///
/// El contenido del Flyout NO hereda el DataContext del UserControl porque
/// vive en un top-level aparte. Por eso lo propagamos manualmente en dos
/// puntos: al cambiar el DataContext del control y al abrir el Flyout.
/// </summary>
public partial class BusSelectorButton : UserControl
{
    public BusSelectorButton()
    {
        InitializeComponent();

        // Propagar el DataContext al contenido del Flyout cuando cambie.
        DataContextChanged += (_, _) => ApplyDataContextToFlyout();
    }

    /// <summary>Se dispara al abrir el Flyout. Reaplica el DataContext por las dudas.</summary>
    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        ApplyDataContextToFlyout();
    }

    /// <summary>
    /// Copia el DataContext del UserControl al contenido del Flyout.
    /// El Flyout se obtiene del Button (el compilador de XAML no genera
    /// un campo para x:Name dentro de un Flyout porque vive en otro scope).
    /// </summary>
    private void ApplyDataContextToFlyout()
    {
        if (SelectorButton?.Flyout is not Flyout flyout) return;
        if (flyout.Content is Control content)
            content.DataContext = this.DataContext;
    }
}