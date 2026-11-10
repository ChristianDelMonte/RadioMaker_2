using Avalonia;
using QuestPDF.Infrastructure;

namespace RadioMaker.UI;

internal static class Program
{
    // Inicialización temprana. No usar APIs de Avalonia antes de AppMain.
    [STAThread]
    public static void Main(string[] args)
    {
        // Configurar la licencia de QuestPDF UNA SOLA VEZ al arranque.
        // Antes se seteaba en LogExporter.ExportPdf en cada llamada, lo
        // que podía race-condicionar si dos exportaciones coincidían en
        // tiempo (QuestPDF.Settings.License no es thread-safe al setear).
        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }
        catch
        {
            // Best-effort: si falla, LogExporter lo vuelve a intentar por
            // si la inicialización temprana no llegó a tomar.
        }

        // Migrar datos de la carpeta vieja a la nueva (por el renombrado
        // RmsAudioPlayer -> RadioMaker). Se hace ANTES de cualquier
        // Load para que todos los Store encuentren los archivos.
        try { Services.UserDataMigrator.MigrateIfNeeded(); } catch { }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Configuración del AppBuilder para el diseñador y el runtime.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}