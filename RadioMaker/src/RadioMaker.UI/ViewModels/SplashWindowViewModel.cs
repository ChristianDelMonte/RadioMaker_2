using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.UI.Services;
using System;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del splash screen. Solo expone dos propiedades que se
/// actualizan desde el orquestador de arranque (App.axaml.cs):
///   - Progress: 0.0 .. 1.0 para la barra de onda.
///   - StatusText: mensaje de que se esta cargando.
/// -------------
public sealed partial class SplashWindowViewModel : ViewModelBase
{
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "Iniciando...";

    /// -------------Nombre de la app que se muestra en el splash.</summary>
    public string AppName => "RadioMaker";

    /// -------------Eslogan que se muestra debajo del nombre.</summary>
    public string AppSlogan => "Forever FREE Radio Automation Software!";

    /// -------------Version de la app auto-generada en cada build.</summary>
    public string VersionText => Services.AppVersionInfo.VersionText;

    /// -------------Fecha/hora de compilacion auto-generada.</summary>
    public string BuildText => Services.AppVersionInfo.BuildText;

    /// -------------
    /// Imagen de fondo del splash (elegida al azar al arranque de la app).
    /// Es la misma que se muestra en la ventana "Acerca de..." durante
    /// toda la sesion.
    ///
    /// Es un Bitmap (no un string) porque Avalonia no convierte strings
    /// a Bitmap automaticamente cuando vienen por Binding.
    /// -------------
    public Bitmap? SplashImage => SplashImageProvider.CurrentBitmap;
}