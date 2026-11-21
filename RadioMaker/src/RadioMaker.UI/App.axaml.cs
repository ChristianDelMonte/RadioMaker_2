using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RadioMaker.Core.Engine;
using RadioMaker.Core.Logging;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Views;

namespace RadioMaker.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Inicializar el servicio de temas ANTES de crear cualquier ventana.
        ThemeService.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Arrancar el flujo de inicializacion con splash en background.
            // No bloqueamos el arranque del bucle de UI.
            _ = InitializeWithSplashAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// -------------
    /// Flujo de arranque con splash:
    ///   1. Mostrar el splash.
    ///   2. Inicializar el motor de audio (BassEngine + Initialize).
    ///   3. Crear el ViewModel principal.
    ///   4. Restaurar el workspace.
    ///   5. Crear y mostrar la MainWindow.
    ///   6. Cerrar el splash.
    ///
    /// Cada paso reporta progreso al splash. Los pasos pesados corren en
    /// Task.Run; los pasos que tocan UI corren en el hilo de UI.
    /// -------------
    private async Task InitializeWithSplashAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splash = new SplashWindow();
        splash.Show();
        splash.ReportProgress(0.0, "Iniciando RadioMaker...");

        // Dejar que el splash se renderice una vez antes de arrancar la carga.
        await Task.Delay(300);

        BassEngine? engine = null;
        MainWindowViewModel? viewModel = null;

        try
        {
            // ── Paso 1: motor de audio ─────────────────────────────────
            splash.ReportProgress(0.05, "Inicializando motor de audio...");
            await Task.Delay(250);

            engine = await Task.Run(() => new BassEngine());

            splash.ReportProgress(0.25, "Inicializando BASS...");
            await Task.Delay(250);

            await Task.Run(() =>
            {
                try { engine.Initialize(deviceIndex: -1, sampleRate: 44100); }
                catch (Exception ex)
                {
                    AppLogger.Warning("Splash",
                        $"Fallo la inicializacion de BASS: {ex.Message}");
                }
            });

            // ── Paso 2: plugins / formatos ─────────────────────────────
            splash.ReportProgress(0.45, "Cargando plugins de formato...");
            await Task.Delay(250);

            // ── Paso 3: configuracion ─────────────────────────────────
            splash.ReportProgress(0.60, "Cargando configuracion...");
            await Task.Delay(250);

            viewModel = new MainWindowViewModel(engine);

            // ── Paso 4: workspace ─────────────────────────────────────
            splash.ReportProgress(0.78, "Restaurando workspace...");
            await Task.Delay(250);

            try { viewModel.RestoreWorkspace(); }
            catch (Exception ex)
            {
                AppLogger.Warning("Splash",
                    $"No se pudo restaurar el workspace: {ex.Message}");
            }

            // ── Paso 5: interfaz ──────────────────────────────────────
            splash.ReportProgress(0.92, "Preparando interfaz...");
            await Task.Delay(250);

            var mainWindow = new MainWindow { DataContext = viewModel };

            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += (_, _) =>
            {
                try { viewModel.SaveWorkspace(); } catch { }
                try { AppLogger.Service.Dispose(); } catch { }
                try { engine.Dispose(); } catch { }
            };

            mainWindow.Show();

            // ── Paso 6: cierre del splash ─────────────────────────────
            splash.ReportProgress(1.0, "Listo");
            await Task.Delay(350);

            splash.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Splash", "Error critico durante el arranque", ex);

            splash.ReportProgress(1.0, $"Error al iniciar: {ex.Message}");
            await Task.Delay(2500);

            try { splash.Close(); } catch { }
        }
    }
}