using System;
using Material.Icons;

namespace RadioMaker.UI.Services;

/// -------------
/// Un tip de ayuda que se muestra en la barra de estado inferior.
/// -------------
public sealed record AppTip(MaterialIconKind Icon, string Message);

/// -------------
/// Lista central de tips de ayuda. Se rotan cada 90 segundos en la barra
/// de estado para que el operador vaya descubriendo funcionalidades.
///
/// Para agregar un tip nuevo: agregar una línea al array <see cref="Tips"/>.
/// -------------
public static class AppTips
{
    private static readonly AppTip[] Tips =
    {
        new(MaterialIconKind.Microphone,
            "Configura las locuciones de hora y clima activando el Addon RMVoice."),

        new(MaterialIconKind.Waveform,
            "Crea proyectos multipista con el Addon RMWave y reprodúcelos directamente desde RadioMaker."),

        new(MaterialIconKind.SwapHorizontal,
            "Activa el crossfade en Configuración → Fade para transiciones suaves entre pistas de Playlist."),

        new(MaterialIconKind.Drag,
            "Arrastra archivos desde el explorador a cualquier pestaña para agregarlos rápidamente."),

        new(MaterialIconKind.CursorDefaultClick,
            "Click derecho sobre cualquier ítem para editar sus propiedades u opciones adicionales."),

        new(MaterialIconKind.Star,
            "Guarda tus carpetas favoritas para acceso rápido desde el panel derecho del explorador."),

        new(MaterialIconKind.Shield,
            "Configura las Políticas de exclusividad para evitar solapamientos entre módulos del mismo bus."),

        new(MaterialIconKind.Alert,
            "Activa la Emergencia en Configuración → Comportamiento para evitar silencios al aire."),

        new(MaterialIconKind.Tag,
            "Click derecho sobre un audio para editar sus tags (ID3, FLAC, M4A, OGG)."),

        new(MaterialIconKind.AutoFix,
            "Usa el Generador de Playlist para armar listas por duración o cantidad automáticamente."),

        new(MaterialIconKind.NoteText,
            "Las Notas se guardan automáticamente: no pierdas tus ideas."),

        new(MaterialIconKind.SpeakerMultiple,
            "Crea varios buses de salida para preescuchar en auriculares mientras el aire sigue sonando."),

        new(MaterialIconKind.Speedometer,
            "El análisis de trim y normalización se cachea por archivo: la segunda reproducción es instantánea."),

        new(MaterialIconKind.Tune,
            "Configura el EQ de 10 bandas y guarda tus propios presets personalizados."),

        new(MaterialIconKind.FileMusic,
            "En la botonera, cada pestaña tiene su propio bus de salida. Ideal para separar el aire de la preescucha."),

        new(MaterialIconKind.ClockOutline,
            "En Programados, cada lista puede activarse o desactivarse con el switch de la barra superior."),

        new(MaterialIconKind.MusicNote,
            "El crossfade entre pistas del Playlist usa el mismo valor configurado en Fade."),

        new(MaterialIconKind.FolderOpen,
            "Presioná el switch \"Activa\" en una lista de Programados mientras la armás para que no se dispare."),

        new(MaterialIconKind.Bell,
            "Configura el beep de emergencia cada N pistas para que el operador detecte la alerta."),

        new(MaterialIconKind.SkipNext,
            "En Playlist podés usar Saltar al siguiente en lugar de Continuar cuando un programado interrumpe."),
    };

    private static readonly Random Rng = new();

    public static int Count => Tips.Length;

    /// -------------Tip por índice (con wrap-around).</summary>
    public static AppTip GetByIndex(int index)
    {
        if (Tips.Length == 0)
            return new AppTip(MaterialIconKind.InformationOutline, "");
        var i = ((index % Tips.Length) + Tips.Length) % Tips.Length;
        return Tips[i];
    }

    /// -------------Tip aleatorio.</summary>
    public static AppTip GetRandom()
    {
        if (Tips.Length == 0)
            return new AppTip(MaterialIconKind.InformationOutline, "");
        return Tips[Rng.Next(Tips.Length)];
    }
}