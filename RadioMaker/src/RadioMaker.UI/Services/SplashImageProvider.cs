using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RadioMaker.UI.Services;

/// <summary>
/// Provee una imagen splash aleatoria entre varias opciones.
///
/// La eleccion se hace UNA vez, cuando la clase se inicializa (la primera
/// vez que alguien la toca). Esa imagen se mantiene durante toda la sesion
/// de la app, de modo que el splash screen y la ventana "Acerca de..."
/// muestren la misma imagen.
///
/// Al reiniciar la app se vuelve a elegir al azar.
///
/// Uso:
///     var bitmap = SplashImageProvider.CurrentBitmap; // Bitmap listo para usar
///     var uri    = SplashImageProvider.CurrentUri;    // "avares://.../Assets/splash.png"
/// </summary>
public static class SplashImageProvider
{
    /// <summary>
    /// Lista de imagenes disponibles (rutas relativas a la raiz del proyecto,
    /// tal como estan en la carpeta Assets/).
    ///
    /// Para agregar una imagen nueva, simplemente agregala a esta lista
    /// y ponela en Assets/.
    /// </summary>
    private static readonly string[] AvailableImages =
    {
        "/Assets/splash.png",
        "/Assets/splash-1.png",
        "/Assets/splash-2.png",
        "/Assets/splash-3.png"
    };

    private static readonly object Gate = new();
    private static string? _currentUri;
    private static Bitmap? _currentBitmap;

    /// <summary>
    /// Ruta relativa al recurso de la imagen elegida para esta sesion.
    /// Se elige la primera vez que se accede.
    /// </summary>
    public static string CurrentUri
    {
        get
        {
            EnsureChosen();
            return _currentUri!;
        }
    }

    /// <summary>
    /// Bitmap ya cargado de la imagen elegida.
    /// Se carga UNA vez y se reutiliza en toda la sesion.
    /// </summary>
    public static Bitmap? CurrentBitmap
    {
        get
        {
            EnsureChosen();
            return _currentBitmap;
        }
    }

    /// <summary>
    /// Fuerza una nueva eleccion aleatoria. Util para tests o para
    /// previsualizar las imagenes.
    /// </summary>
    public static void Reroll()
    {
        lock (Gate)
        {
            _currentUri = PickRandom();
            _currentBitmap = TryLoadBitmap(_currentUri);
        }
    }

    private static void EnsureChosen()
    {
        if (_currentUri != null) return;

        lock (Gate)
        {
            if (_currentUri != null) return;

            _currentUri = PickRandom();
            _currentBitmap = TryLoadBitmap(_currentUri);
        }
    }

    private static string PickRandom()
    {
        if (AvailableImages.Length == 0)
            return "/Assets/splash.png";

        int idx = Random.Shared.Next(AvailableImages.Length);
        return AvailableImages[idx];
    }

    /// <summary>
    /// Carga un Bitmap desde una ruta de asset de Avalonia ("/Assets/...").
    /// Devuelve null si el archivo no existe o falla la carga.
    /// </summary>
    private static Bitmap? TryLoadBitmap(string relativePath)
    {
        try
        {
            var uri = new Uri("avares://RadioMaker" + relativePath);
            if (!AssetLoader.Exists(uri))
                return null;

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}