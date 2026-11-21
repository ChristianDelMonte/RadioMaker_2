using System.Text.Json.Serialization;
using RadioMaker.Core.Playback;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Tipo de contenido del mixado intermedio.
/// -------------
public enum MixContentType
{
    Audio = 0,
    Locution = 1
}

/// -------------
/// Tipo de ítem de la playlist:
///   - Audio: un archivo de audio reproducible.
///   - Locution: un comando de locución ejecutado por RMVoice
///     (hora, clima, hora+clima).
/// -------------
public enum PlaylistItemKind
{
    Audio = 0,
    Locution = 1
}

public sealed class PlaylistTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// -------------
    /// Tipo de ítem. Los .rmf viejos no tienen este campo → default Audio
    /// (retrocompatibilidad).
    /// -------------
    public PlaylistItemKind ItemKind { get; set; } = PlaylistItemKind.Audio;

    /// -------------
    /// Ruta del archivo de audio. Solo aplica cuando ItemKind == Audio.
    /// -------------
    public string FilePath { get; set; } = string.Empty;

    /// -------------
    /// Comando de locución. Solo aplica cuando ItemKind == Locution.
    /// -------------
    public LocutionCommand LocutionCommand { get; set; } = LocutionCommand.HourAndMinute;

    public string Title { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public double VolumeDb { get; set; } = 0.0;
    public double DurationSeconds { get; set; } = 0.0;

    /// -------------
    /// Duracion estimada de una locucion (en segundos). Se usa para el
    /// calculo del total de la playlist, porque la duracion real solo se
    /// conoce cuando el engine la reproduce. No afecta la reproduccion.
    /// -------------
    public double EstimatedDurationSeconds { get; set; } = 5.0;

    // ───── Mixado intermedio ─────────────────────────────────────────────

    public MixContentType MixContentType { get; set; } = MixContentType.Audio;
    public string MixAudioPath { get; set; } = string.Empty;
    public LocutionCommand MixLocutionCommand { get; set; } = LocutionCommand.HourAndMinute;
    public double MixStartSeconds { get; set; } = 0.0;
    public double MixVolumeDb { get; set; } = -12.0;
    public double MixDuckLevel { get; set; } = 0.3;

    public string Notes { get; set; } = string.Empty;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    [JsonIgnore]
    public bool IsAudioItem => ItemKind == PlaylistItemKind.Audio;

    [JsonIgnore]
    public bool IsLocutionItem => ItemKind == PlaylistItemKind.Locution;

    /// -------------
    /// Titulo a mostrar en la UI. Para locuciones, describe el comando.
    /// -------------
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (ItemKind == PlaylistItemKind.Locution)
            {
                var cmdText = LocutionCommand switch
                {
                    LocutionCommand.HourAndMinute => "Hora + minutos",
                    LocutionCommand.Climate => "Clima (temp + humedad)",
                    LocutionCommand.HourAndClimate => "Hora + clima",
                    _ => "Locución"
                };

                return !string.IsNullOrWhiteSpace(Title)
                    ? Title
                    : $"Locución: {cmdText}";
            }

            return !string.IsNullOrWhiteSpace(Title)
                ? Title
                : (!string.IsNullOrWhiteSpace(FilePath)
                    ? Path.GetFileName(FilePath)
                    : "(sin archivo)");
        }
    }

    /// -------------
    /// True si el item tiene mixado configurado. No aplica a locuciones.
    /// -------------
    [JsonIgnore]
    public bool HasMix
    {
        get
        {
            if (ItemKind == PlaylistItemKind.Locution) return false;

            return MixContentType switch
            {
                MixContentType.Audio => !string.IsNullOrWhiteSpace(MixAudioPath),
                MixContentType.Locution => true,
                _ => false
            };
        }
    }

    [JsonIgnore]
    public string MixDisplayName => MixContentType switch
    {
        MixContentType.Audio => !string.IsNullOrWhiteSpace(MixAudioPath)
            ? Path.GetFileName(MixAudioPath)
            : "(sin mixado)",
        MixContentType.Locution => MixLocutionCommand switch
        {
            LocutionCommand.HourAndMinute => "Locución: Hora + minutos",
            LocutionCommand.Climate => "Locución: Clima",
            LocutionCommand.HourAndClimate => "Locución: Hora + clima",
            _ => "Locución"
        },
        _ => "(sin mixado)"
    };

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            if (ItemKind == PlaylistItemKind.Locution)
            {
                return EstimatedDurationSeconds > 0.5
                    ? $"Duración estimada: {AudioFileInfoReader.FormatDuration(EstimatedDurationSeconds)}"
                    : "Locución (sin duración estimada)";
            }

            return AudioFileInfoReader.FormatDuration(DurationSeconds);
        }
    }
}