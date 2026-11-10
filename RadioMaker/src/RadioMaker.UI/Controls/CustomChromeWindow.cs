using System;
using Avalonia.Controls;

namespace RadioMaker.UI.Controls;

/// <summary>
/// Clase base para ventanas con barra de titulo personalizada.
///
/// Configura la ventana para usar chrome custom (sin decoraciones del
/// sistema) en Windows y macOS, y deja el chrome nativo en Linux, donde
/// el modo extendido de Avalonia no funciona de forma fiable.
///
/// Cada ventana que herede de esta clase debe:
///   1. Cambiar el nodo raiz del .axaml de <Window> a <controls:CustomChromeWindow>.
///   2. Heredar del code-behind.
///   3. Agregar un control <controls:ModalTitleBar> en su layout.
/// </summary>
public abstract class CustomChromeWindow : Window
{
    protected CustomChromeWindow()
    {
        ConfigurePlatformChrome();
    }

    /// <summary>
    /// True si el chrome custom esta activo (Windows/macOS). False en Linux.
    /// Se puede usar para condicionar la visibilidad de la barra custom.
    /// </summary>
    public bool IsCustomChromeEnabled => !OperatingSystem.IsLinux();

    private void ConfigurePlatformChrome()
    {
        if (OperatingSystem.IsLinux())
        {
            // Linux: usar la barra nativa del sistema.
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = false;
        }
        else
        {
            // Windows / macOS: chrome 100% personalizado.
            WindowDecorations = WindowDecorations.None;
            ExtendClientAreaToDecorationsHint = true;
        }
    }
}