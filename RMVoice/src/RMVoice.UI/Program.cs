using Avalonia;
using RMVoice.UI.Services;

namespace RMVoice.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SingleInstance.TryAcquire(out var instance))
        {
            // Ya hay una instancia corriendo: notificarla y salir.
            SingleInstance.SignalExisting();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}