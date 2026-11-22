namespace RadioMaker.Core.Logging;

/// -------------
/// Punto de acceso global al LogService de la aplicacion.
///
/// Cualquier modulo (AudioCore, UI, reproductores, scheduler) puede escribir
/// log a traves de AppLogger sin necesidad de recibir el LogService por
/// inyeccion de dependencias.
///
/// Es un singleton, creado la primera vez que se accede. Su ciclo de vida
/// dura toda la ejecucion de la app.
/// -------------
public static class AppLogger
{
    private static readonly LogService _service = new();

    /// -------------
    /// Instancia compartida del LogService. La UI la usa para suscribirse
    /// a las entradas y mostrar el log en vivo.
    /// -------------
    public static LogService Service => _service;

    // ───── Conveniencia ──────────────────────────────────────────────────

    public static void Debug(string category, string message)
        => _service.Debug(category, message);

    public static void Info(string category, string message)
        => _service.Info(category, message);

    public static void Warning(string category, string message)
        => _service.Warning(category, message);

    public static void Warning(string category, string message, Exception ex)
        => _service.Warning(category, message, ex);

    public static void Error(string category, string message)
        => _service.Error(category, message);

    public static void Error(string category, string message, Exception ex)
        => _service.Error(category, message, ex);
}
