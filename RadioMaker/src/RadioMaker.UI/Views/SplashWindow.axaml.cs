using Avalonia.Controls;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

/// -------------
/// Splash screen de RadioMaker.
///
/// Se muestra al arrancar la app, mientras se inicializa el motor de audio,
/// se carga la configuracion y se prepara la ventana principal.
///
/// El orquestador (App.axaml.cs) llama a ReportProgress(progress, status)
/// desde cualquier hilo. El metodo marshaléa al UI thread automaticamente.
/// -------------
public partial class SplashWindow : Window
{
    private readonly SplashWindowViewModel _vm = new();

    public SplashWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    /// -------------
    /// Actualiza el progreso y el texto de estado.
    /// Es seguro llamarlo desde cualquier hilo.
    /// -------------
    /// <param name="progress">Valor entre 0.0 (fondo) y 1.0 (cima).</param>
    /// <param name="status">Texto a mostrar debajo de la imagen.</param>
    public void ReportProgress(double progress, string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Post(() => ReportProgress(progress, status));
            return;
        }

        _vm.Progress = Math.Clamp(progress, 0.0, 1.0);
        if (!string.IsNullOrWhiteSpace(status))
            _vm.StatusText = status;
    }
}