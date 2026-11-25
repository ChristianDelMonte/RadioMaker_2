using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RMVoice.UI.Services;
using RMVoice.UI.ViewModels;
using RMVoice.UI.Views;
using RMVoice.Core;
using RMVoice.UI.Localization;

namespace RMVoice.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Inicializar el servicio de tema ANTES de crear la ventana.
        ThemeService.Initialize();
        VoiceLocalizationService.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            var announcer = new Announcer(new AnnouncerOptions
            {
                OwnsBass = true,
                UserAgent = "RMVoice.App/1.0",
                InterfaceLanguage = VoiceLocalizationService.CurrentCulture.TwoLetterISOLanguageName
            });

            try
            {
                announcer.Initialize();
            }
            catch
            {
                // La inicializacion puede fallar si no hay dispositivo de audio.
                // El ViewModel mostrara el error en StatusMessage.
            }

            var vm = new MainWindowViewModel(new DialogService(window), announcer);

            window.DataContext = vm;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                vm.Dispose();
                announcer.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
