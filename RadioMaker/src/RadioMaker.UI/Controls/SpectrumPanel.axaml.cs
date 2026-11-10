using Avalonia.Controls;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Code-behind del panel Spectrum.
/// No tiene logica propia: toda la interaccion se maneja via bindings
/// con el SpectrumPanelViewModel, y el SpectrumAnalyzer controla su
/// propio timer y dibujo.
/// </summary>
public partial class SpectrumPanel : UserControl
{
    public SpectrumPanel()
    {
        InitializeComponent();
    }
}