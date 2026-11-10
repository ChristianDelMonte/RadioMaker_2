using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace RMVoice.UI.Services;

/// <summary>
/// Sistema de instancia única del EXE.
///
/// Funcionamiento:
///  • El primer proceso crea un Mutex global y un NamedPipe server.
///  • Los procesos siguientes NO pueden tomar el Mutex, se conectan al pipe,
///    envían "SHOW" y terminan.
///  • El primer proceso recibe "SHOW" y trae su ventana al frente.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Global\\RMVoice.SingleInstance.Mutex";
    private const string PipeName = "RMVoice.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _serverTask = Task.Run(RunPipeServerAsync);
    }

    /// <summary>
    /// Intenta adquirir la instancia única. Devuelve <c>true</c> si es el primer
    /// proceso; en ese caso <paramref name="instance"/> queda inicializado.
    /// Devuelve <c>false</c> si ya hay otra instancia (y <paramref name="instance"/>
    /// queda a <c>null</c>).
    /// </summary>
    public static bool TryAcquire(out SingleInstance? instance)
    {
        var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var isFirst);

        if (isFirst)
        {
            instance = new SingleInstance(mutex);
            return true;
        }

        mutex.Dispose();
        instance = null;
        return false;
    }

    /// <summary>Notifica al primer proceso que se traiga al frente.</summary>
    public static void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.None);

            client.Connect(500);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("SHOW");
        }
        catch
        {
            // Si no podemos conectar, no es grave: el proceso simplemente sale.
        }
    }

    private async Task RunPipeServerAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (line?.Trim() == "SHOW")
                    Dispatcher.UIThread.Post(BringToFront);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignorar y reintentar.
            }
        }
    }

    private static void BringToFront()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is null) return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();

        // La llamada a SetForegroundWindow solo se ejecuta en Windows.
        if (OperatingSystem.IsWindows())
            TryForceForeground(window);
    }

    private static void TryForceForeground(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
                SetForegroundWindow(handle);
        }
        catch
        {
            // Silencioso.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void Dispose()
    {
        _cts.Cancel();

        try { _serverTask.Wait(500); } catch { }

        _cts.Dispose();

        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}