using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;

namespace RMWave.UI.Themes;

/// <summary>
/// Definición de un tema: colores para controles estándar y para
/// los elementos dibujados por código (timeline, clips, waveforms).
/// </summary>
public class AppTheme
{
    public string Name { get; set; } = "Oscuro";
    public bool IsDark { get; set; } = true;

    public string AppBackground { get; set; } = "#1E1E1E";
    public string PanelBackground { get; set; } = "#252526";
    public string CanvasBackground { get; set; } = "#191919";
    public string ClipBackground { get; set; } = "#1E2832";
    public string ClipText { get; set; } = "#FFFFFF";
    public string Waveform { get; set; } = "#46B4DC";
    public string Fade { get; set; } = "#46FFA500";
    public string GridLine { get; set; } = "#323232";
    public string RulerLine { get; set; } = "#32969696";
    public string RulerText { get; set; } = "#8CA0A0A0";
    public string Accent { get; set; } = "#0078D7";
    public string Text { get; set; } = "#F0F0F0";

    // NUEVO: Colores del rango de loop.
    public string LoopFill { get; set; } = "#1EFFC800";   // Rectángulo (por defecto ámbar semitransparente).
    public string LoopBorder { get; set; } = "#A0FFA500"; // Líneas de borde (por defecto ámbar más opaco).

    public SolidColorBrush GetAppBrush() => new SolidColorBrush(Color.Parse(AppBackground));
    public SolidColorBrush GetPanelBrush() => new SolidColorBrush(Color.Parse(PanelBackground));
    public SolidColorBrush GetCanvasBrush() => new SolidColorBrush(Color.Parse(CanvasBackground));
    public SolidColorBrush GetClipBrush() => new SolidColorBrush(Color.Parse(ClipBackground));
    public SolidColorBrush GetClipTextBrush() => new SolidColorBrush(Color.Parse(ClipText));
    public SolidColorBrush GetWaveformBrush() => new SolidColorBrush(Color.Parse(Waveform));
    public SolidColorBrush GetFadeBrush() => new SolidColorBrush(Color.Parse(Fade));
    public SolidColorBrush GetGridLineBrush() => new SolidColorBrush(Color.Parse(GridLine));
    public SolidColorBrush GetRulerLineBrush() => new SolidColorBrush(Color.Parse(RulerLine));
    public SolidColorBrush GetRulerTextBrush() => new SolidColorBrush(Color.Parse(RulerText));
    public SolidColorBrush GetAccentBrush() => new SolidColorBrush(Color.Parse(Accent));
    public SolidColorBrush GetTextBrush() => new SolidColorBrush(Color.Parse(Text));
    public SolidColorBrush GetLoopFillBrush() => new SolidColorBrush(Color.Parse(LoopFill));
    public SolidColorBrush GetLoopBorderBrush() => new SolidColorBrush(Color.Parse(LoopBorder));
}

/// <summary>
/// Administra los temas disponibles, aplica el tema actual y lo persiste en disco.
/// </summary>
public static class ThemeManager
{
    public static readonly List<AppTheme> Available = new()
    {
        // 1) Oscuro clásico (el actual)
        new AppTheme
        {
            Name = "Oscuro",
            IsDark = true,
            AppBackground = "#1E1E1E", PanelBackground = "#252526",
            CanvasBackground = "#191919", ClipBackground = "#1E2832",
            ClipText = "#FFFFFF", Waveform = "#46B4DC", Fade = "#46FFA500",
            GridLine = "#323232", RulerLine = "#32969696", RulerText = "#8CA0A0A0",
            Accent = "#0078D7", Text = "#F0F0F0"
        },

        // 2) Claro
        new AppTheme
        {
            Name = "Claro",
            IsDark = false,
            AppBackground = "#F5F5F5", PanelBackground = "#FFFFFF",
            CanvasBackground = "#E4E4E4", ClipBackground = "#CFE0EF",
            ClipText = "#14283C", Waveform = "#0B6FA4", Fade = "#55E08A00",
            GridLine = "#C8C8C8", RulerLine = "#40606060", RulerText = "#90505050",
            Accent = "#0078D7", Text = "#1E1E1E"
        },

        // 3) Azul medianoche
        new AppTheme
        {
            Name = "Azul Medianoche",
            IsDark = true,
            AppBackground = "#0D1117", PanelBackground = "#161B22",
            CanvasBackground = "#0A0E14", ClipBackground = "#14243A",
            ClipText = "#E6EDF3", Waveform = "#58A6FF", Fade = "#46FF9E3D",
            GridLine = "#21262D", RulerLine = "#328B949E", RulerText = "#8C8B949E",
            Accent = "#58A6FF", Text = "#E6EDF3"
        },

        // 4) Estudio cálido
        new AppTheme
        {
            Name = "Estudio Cálido",
            IsDark = true,
            AppBackground = "#1F1A16", PanelBackground = "#2A2320",
            CanvasBackground = "#191411", ClipBackground = "#33261C",
            ClipText = "#F5E9DC", Waveform = "#E8A33D", Fade = "#46FF6B3D",
            GridLine = "#3A322C", RulerLine = "#32A89484", RulerText = "#8CA89484",
            Accent = "#E8A33D", Text = "#F0E8E0",

            // NUEVO: Celeste agua para contrastar con los tonos cálidos.
            LoopFill = "#327FD4E8",
            LoopBorder = "#C87FD4E8"

            // Verde Menta
            //LoopFill = "#32A8E6CF",
            //LoopBorder = "#C8A8E6CF"

            // Verde Lima suave
            //LoopFill = "#32B5E89C",
            //LoopBorder = "#C8B5E89C"
        }
    };

    public static AppTheme Current { get; private set; } = Available[0];

    public static event Action? ThemeChanged;

    private static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "rmwave_theme.cfg");

    /// <summary>Aplica un tema: variante claro/oscuro + recursos globales + notificación.</summary>
    public static void Apply(AppTheme theme, bool save = true)
    {
        Current = theme;

        if (Application.Current != null)
        {
            // Cambia la variante de todos los controles estándar (menús, botones, sliders...).
            Application.Current.RequestedThemeVariant =
                theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

            // Recursos globales para el AXAML que use DynamicResource.
            Application.Current.Resources["AppBackgroundBrush"] = theme.GetAppBrush();
            Application.Current.Resources["PanelBackgroundBrush"] = theme.GetPanelBrush();
            Application.Current.Resources["AccentBrush"] = theme.GetAccentBrush();
            Application.Current.Resources["TextPrimaryBrush"] = theme.GetTextBrush();
        }

        if (save) Save();
        ThemeChanged?.Invoke();
    }

    public static void Save()
    {
        try { File.WriteAllText(SettingsPath, Current.Name); }
        catch { }
    }

    /// <summary>Carga el tema guardado (o aplica el oscuro por defecto).</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var name = File.ReadAllText(SettingsPath).Trim();
                var theme = Available.Find(t => t.Name == name);
                if (theme != null)
                {
                    Apply(theme, save: false);
                    return;
                }
            }
        }
        catch { }

        Apply(Available[0], save: false);
    }
}