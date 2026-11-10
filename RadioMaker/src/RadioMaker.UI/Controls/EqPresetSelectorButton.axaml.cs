using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Selector compacto del preset del ecualizador. Muestra un icono, el
/// nombre del preset activo y una flechita; al hacer click abre un flyout
/// con la lista de presets (fabrica + personalizados).
///
/// El DataContext debe ser un <see cref="ViewModels.EqualizerPanelViewModel"/>.
///
/// Igual que BusSelectorButton, el Flyout vive en un top-level aparte y no
/// hereda el DataContext, por lo que lo propagamos manualmente.
/// </summary>
public partial class EqPresetSelectorButton : UserControl
{
    public EqPresetSelectorButton()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => ApplyDataContextToFlyout();
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        ApplyDataContextToFlyout();
    }

    private void ApplyDataContextToFlyout()
    {
        if (SelectorButton?.Flyout is not Flyout flyout) return;
        if (flyout.Content is Control content)
            content.DataContext = this.DataContext;
    }
}