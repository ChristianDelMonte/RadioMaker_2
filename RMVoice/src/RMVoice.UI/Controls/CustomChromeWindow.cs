using Avalonia.Controls;

namespace RMVoice.UI.Controls;

/// <summary>
/// Base para ventanas con barra de titulo custom. Configura el chrome
/// (WindowDecorations=None + ExtendClientArea) en Windows/macOS, y deja el
/// chrome nativo en Linux.
/// </summary>
public abstract class CustomChromeWindow : Window
{
    protected CustomChromeWindow()
    {
        ConfigurePlatformChrome();
    }

    public bool IsCustomChromeEnabled => !OperatingSystem.IsLinux();

    private void ConfigurePlatformChrome()
    {
        if (OperatingSystem.IsLinux())
        {
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = false;
        }
        else
        {
            WindowDecorations = WindowDecorations.None;
            ExtendClientAreaToDecorationsHint = true;
        }
    }
}