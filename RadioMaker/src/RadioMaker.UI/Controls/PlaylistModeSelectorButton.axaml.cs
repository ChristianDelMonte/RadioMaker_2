using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Control reutilizable que muestra un boton compacto con el modo de
/// reproduccion actual de la playlist (Una vez / Repetir / Aleatorio)
/// y un flyout para cambiarlo.
///
/// El DataContext debe ser un <see cref="ViewModels.PlaylistEditorViewModel"/>.
///
/// Igual que BusSelectorButton, el contenido del Flyout vive en un top-level
/// aparte y no hereda el DataContext automaticamente, por lo que lo
/// propagamos manualmente.
/// </summary>
public partial class PlaylistModeSelectorButton : UserControl
{
    public PlaylistModeSelectorButton()
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