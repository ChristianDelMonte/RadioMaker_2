using System;
using Avalonia.Controls;

namespace RMWave.UI.Controls;

/// <summary>
/// Base para ventanas con barra de titulo custom. Misma implementacion que
/// AnnouncerClock.Controls.CustomChromeWindow, con namespace propio.
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