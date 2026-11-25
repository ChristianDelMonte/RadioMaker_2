using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RadioMaker.Core.Engine;
using RadioMaker.Core.Logging;
using RadioMaker.UI.Services;
using RadioMaker.UI.Configuration;
using RadioMaker.UI.Localization;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Views;

namespace RadioMaker.UI;

public partial class App : Application
{
    private static bool _globalExceptionHooksRegistered;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalExceptionLogging();

        // Inicializar el servicio de temas ANTES de crear cualquier ventana.
        ThemeService.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // La selección inicial, si corresponde, se muestra antes del splash.
            _ = InitializeApplicationAsync(desktop);
        }
        else
        {
            LocalizationService.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeApplicationAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!LanguageSettingsStore.TryLoad(out _))
        {
            string? selectedLanguage = null;
            try
            {
                selectedLanguage = await ShowFirstRunLanguageWindowAsync(desktop);
            }
            catch
            {
                // If the chooser cannot be shown, use the system language and continue startup.
            }

            LanguageSettingsStore.Save(selectedLanguage ?? LanguageSettingsStore.DetectSystemLanguage());
        }

        LocalizationService.Initialize();
        await InitializeWithSplashAsync(desktop);
    }

    private static Task<string?> ShowFirstRunLanguageWindowAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new FirstRunLanguageWindow();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        window.LanguageSelected += language =>
        {
            completion.TrySetResult(language);
            window.Close();
        };
        window.Closed += (_, _) => completion.TrySetResult(null);

        desktop.MainWindow = window;
        window.Show();
        return completion.Task;
    }

    private static void RegisterGlobalExceptionLogging()
    {
        if (_globalExceptionHooksRegistered) return;
        _globalExceptionHooksRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLogger.Error("APP UNHANDLED", $"Excepción no controlada (terminating={args.IsTerminating})", ex);
            else
                AppLogger.Error("APP UNHANDLED", $"Error no controlado (terminating={args.IsTerminating}): {args.ExceptionObject}");

            if (args.IsTerminating)
            {
                try { AppLogger.Service.Dispose(); } catch { }
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("TASK UNOBSERVED", "Excepción de una tarea no observada", args.Exception);
        };
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
        AppLogger.Info("APP", "Inicio de RadioMaker.");
        var splash = new SplashWindow();
        splash.Show();
        splash.ReportProgress(0.0, LocalizationService.GetString("Loc.Splash.StartingRadioMaker"));

        // Dejar que el splash se renderice una vez antes de arrancar la carga.
        await Task.Delay(300);

        BassEngine? engine = null;
        MainWindowViewModel? viewModel = null;

        try
        {
            // ── Paso 1: motor de audio ─────────────────────────────────
            splash.ReportProgress(0.05, LocalizationService.GetString("Loc.Splash.InitializingAudioEngine"));
            await Task.Delay(250);

            engine = await Task.Run(() => new BassEngine());

            splash.ReportProgress(0.25, LocalizationService.GetString("Loc.Splash.InitializingBass"));
            await Task.Delay(250);

            await Task.Run(() =>
            {
                try { engine.Initialize(deviceIndex: -1, sampleRate: 44100); }
                catch (Exception ex)
                {
                    AppLogger.Warning("Splash",
                        "Fallo la inicializacion de BASS.", ex);
                }
            });

            // ── Paso 2: plugins / formatos ─────────────────────────────
            splash.ReportProgress(0.45, LocalizationService.GetString("Loc.Splash.LoadingFormatPlugins"));
            await Task.Delay(250);

            // ── Paso 3: configuracion ─────────────────────────────────
            splash.ReportProgress(0.60, LocalizationService.GetString("Loc.Splash.LoadingConfiguration"));
            await Task.Delay(250);

            viewModel = new MainWindowViewModel(engine);

            // ── Paso 4: workspace ─────────────────────────────────────
            splash.ReportProgress(0.78, LocalizationService.GetString("Loc.Splash.RestoringWorkspace"));
            await Task.Delay(250);

            try { viewModel.RestoreWorkspace(); }
            catch (Exception ex)
            {
                AppLogger.Warning("Splash",
                    "No se pudo restaurar el workspace.", ex);
            }

            // ── Paso 5: interfaz ──────────────────────────────────────
            splash.ReportProgress(0.92, LocalizationService.GetString("Loc.Splash.PreparingInterface"));
            await Task.Delay(250);

            var mainWindow = new MainWindow { DataContext = viewModel };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            desktop.ShutdownRequested += (_, _) =>
            {
                try { viewModel.SaveWorkspace(); }
                catch (Exception ex) { AppLogger.Error("APP SHUTDOWN", "No se pudo guardar el workspace al cerrar", ex); }
                try { engine.Dispose(); }
                catch (Exception ex) { AppLogger.Error("APP SHUTDOWN", "Error al liberar el motor de audio", ex); }
                try { AppLogger.Service.Dispose(); } catch { }
            };

            mainWindow.Show();
            AppLogger.Info("APP", "Ventana principal lista.");

            // ── Paso 6: cierre del splash ─────────────────────────────
            splash.ReportProgress(1.0, LocalizationService.GetString("Loc.Splash.Ready"));
            await Task.Delay(350);

            splash.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Splash", "Error critico durante el arranque", ex);

            splash.ReportProgress(1.0, string.Format(LocalizationService.GetString("Loc.Splash.StartError"), ex.Message));
            await Task.Delay(2500);

            try { splash.Close(); } catch { }
        }
    }
}
