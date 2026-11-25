using System;
using Material.Icons;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.Services;

/// -------------
/// Un tip de ayuda que se muestra en la barra de estado inferior.
/// -------------
public sealed record AppTip(MaterialIconKind Icon, string ResourceKey)
{
    public string Message => LocalizationService.GetString(ResourceKey);
}

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
        new(MaterialIconKind.Microphone, "Loc.Tips.RMVoice"),

        new(MaterialIconKind.Waveform, "Loc.Tips.RMWave"),

        new(MaterialIconKind.SwapHorizontal, "Loc.Tips.Crossfade"),

        new(MaterialIconKind.Drag, "Loc.Tips.DragDrop"),

        new(MaterialIconKind.CursorDefaultClick, "Loc.Tips.ContextMenu"),

        new(MaterialIconKind.Star, "Loc.Tips.FavoriteFolders"),

        new(MaterialIconKind.Shield, "Loc.Tips.Exclusivity"),

        new(MaterialIconKind.Alert, "Loc.Tips.Emergency"),

        new(MaterialIconKind.Tag, "Loc.Tips.AudioTags"),

        new(MaterialIconKind.AutoFix, "Loc.Tips.PlaylistGenerator"),

        new(MaterialIconKind.NoteText, "Loc.Tips.Notes"),

        new(MaterialIconKind.SpeakerMultiple, "Loc.Tips.OutputBuses"),

        new(MaterialIconKind.Speedometer, "Loc.Tips.AnalysisCache"),

        new(MaterialIconKind.Tune, "Loc.Tips.Equalizer"),

        new(MaterialIconKind.FileMusic, "Loc.Tips.CartBuses"),

        new(MaterialIconKind.ClockOutline, "Loc.Tips.ScheduledLists"),

        new(MaterialIconKind.MusicNote, "Loc.Tips.PlaylistCrossfade"),

        new(MaterialIconKind.FolderOpen, "Loc.Tips.DisableScheduledList"),

        new(MaterialIconKind.Bell, "Loc.Tips.EmergencyBeep"),

        new(MaterialIconKind.SkipNext, "Loc.Tips.PlaylistInterrupt"),
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
