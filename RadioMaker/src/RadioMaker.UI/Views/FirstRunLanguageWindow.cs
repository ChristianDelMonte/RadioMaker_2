using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RadioMaker.UI.Views;

/// <summary>One-time language choice shown before the startup splash screen.</summary>
public sealed class FirstRunLanguageWindow : Window
{
    public event Action<string>? LanguageSelected;

    public FirstRunLanguageWindow()
    {
        Title = "RadioMaker — Language / Idioma";
        Width = 410;
        Height = 210;
        MinWidth = Width;
        MinHeight = Height;
        MaxWidth = Width;
        MaxHeight = Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var englishButton = new Button
        {
            Content = "English",
            MinWidth = 125,
            Height = 42
        };
        englishButton.Click += (_, _) => LanguageSelected?.Invoke("en");

        var spanishButton = new Button
        {
            Content = "Español",
            MinWidth = 125,
            Height = 42
        };
        spanishButton.Click += (_, _) => LanguageSelected?.Invoke("es");

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the interface language / Selecciona el idioma de la interfaz",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 12,
                        Children = { spanishButton, englishButton }
                    },
                    new TextBlock
                    {
                        Text = "You can change it later in Settings / Podrás cambiarlo luego en Configuración",
                        FontSize = 11,
                        Opacity = 0.75,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = Avalonia.Media.TextAlignment.Center
                    }
                }
            }
        };
    }
}
