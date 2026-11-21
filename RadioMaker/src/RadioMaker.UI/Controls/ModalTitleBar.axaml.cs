using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RadioMaker.UI.Controls;

/// -------------
/// Barra de titulo compacta para ventanas modales. Muestra un icono
/// generico, el titulo de la ventana y un unico boton "Cerrar".
///
/// El titulo se pasa por binding desde la ventana que la contiene:
///
///     <controls:ModalTitleBar WindowTitle="{Binding $parent[Window].Title}" />
///
/// El arrastre de la ventana lo maneja Avalonia automaticamente a traves
/// de WindowDecorationProperties.ElementRole="TitleBar".
/// -------------
public partial class ModalTitleBar : UserControl
{
    /// -------------Titulo a mostrar en la barra.</summary>
    public static readonly StyledProperty<string> WindowTitleProperty =
        AvaloniaProperty.Register<ModalTitleBar, string>(nameof(WindowTitle), string.Empty);

    public string WindowTitle
    {
        get => GetValue(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    public ModalTitleBar()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        // Cerrar la ventana que contiene este control.
        var window = TopLevel.GetTopLevel(this) as Window;
        window?.Close();
    }
}