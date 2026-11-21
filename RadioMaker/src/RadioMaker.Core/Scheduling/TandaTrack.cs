using System.Text.Json.Serialization;
using RadioMaker.Core.Playback;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Una pista individual dentro de una tanda.
///
/// ───── NOTA DE DISEÑO ─────
///
/// Esta clase tiene DOS usos:
///
///   1. Pista de un .tnd NATIVO (creado por el usuario con el editor
///      de tandas). En este uso, ItemKind SIEMPRE es Audio y
///      LocutionCommand/EstimatedDurationSeconds NO se usan. La UI
///      del editor de tandas no expone locuciones ni mixes, así que
///      un .tnd guardado por el usuario nunca los contiene.
///
///   2. Pista de una "tanda VIRTUAL" creada en memoria por
///      PlaybackEngine.ConvertPlaylistToTanda() cuando el scheduler
///      dispara una Playlist programada. En este uso, la pista puede
///      tener ItemKind = Locution (locución suelta de la playlist) o
///      mix de audio / mix de locución, transportando fielmente el
///      contenido de la playlist original.
///
/// Por eso ItemKind, LocutionCommand y EstimatedDurationSeconds existen:
/// son necesarios para el caso 2. NO son un error de modelado.
/// </summary>
public sealed class TandaTrack
{
    /// <summary>Identificador unico de la pista.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Tipo de pista:
    ///   • Audio    → archivo de audio común (usa FilePath). Es el único
    ///                caso válido en un .tnd nativo.
    ///   • Locution → comando de locución de RMVoice (usa LocutionCommand).
    ///                Solo aparece en tandas virtuales creadas desde
    ///                Playlist. La UI del editor de tandas NO permite
    ///                crear pistas de este tipo.
    ///
    /// Default: Audio (retrocompatibilidad con .tnd viejos).
    /// </summary>
    public PlaylistItemKind ItemKind { get; set; } = PlaylistItemKind.Audio;

    /// <summary>Ruta absoluta del archivo de audio. Solo aplica si ItemKind == Audio.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Comando de locución a reproducir. Solo aplica si ItemKind == Locution
    /// (tandas virtuales creadas desde Playlist programada).
    /// </summary>
    public LocutionCommand LocutionCommand { get; set; } = LocutionCommand.HourAndMinute;

    /// <summary>
    /// Duración ESTIMADA de la locución, en segundos. Solo aplica si
    /// ItemKind == Locution. Se usa para el cálculo de duración total de
    /// la tanda virtual y para mostrar el progreso en la UI.
    /// </summary>
    public double EstimatedDurationSeconds { get; set; } = 5.0;

    /// <summary>Título visible. Si está vacío, se usa el nombre del archivo o el comando.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Si está activa. Una pista deshabilitada se saltea.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Ajuste de volumen relativo (dB) sobre el volumen base del bus.</summary>
    public double VolumeDb { get; set; } = 0.0;

    /// <summary>Duración en segundos. Se completa al cargar el archivo (o del comando si es locución).</summary>
    public double DurationSeconds { get; set; } = 0.0;

    /// <summary>Segundos de espera ANTES de esta pista. 0 = sin pausa.</summary>
    public double DelayBeforeSeconds { get; set; } = 0.0;

    /// <summary>Comentarios libres.</summary>
    public string Notes { get; set; } = string.Empty;

    // ───── Mixado intermedio ─────────────────────────────────────────────
    //
    // Igual que ItemKind/LocutionCommand: estos campos SOLO se usan en
    // tandas virtuales creadas desde Playlist programada. La UI del
    // editor de tandas nativas no expone mix, así que un .tnd guardado
    // por el usuario nunca los contiene.

    /// <summary>Tipo del mix: Audio o Locution. Solo aplica en tandas virtuales.</summary>
    public MixContentType MixContentType { get; set; } = MixContentType.Audio;

    /// <summary>Ruta del archivo de mix (audio). Vacio = sin mix de audio.</summary>
    public string MixAudioPath { get; set; } = string.Empty;

    /// <summary>Comando del mix (locución). Solo aplica cuando MixContentType == Locution.</summary>
    public LocutionCommand MixLocutionCommand { get; set; } = LocutionCommand.HourAndMinute;

    /// <summary>Segundos desde el inicio de la pista para disparar el mix.</summary>
    public double MixStartSeconds { get; set; } = 0.0;

    /// <summary>Volumen adicional del mix de audio (dB). Las locuciones van a volumen pleno.</summary>
    public double MixVolumeDb { get; set; } = -12.0;

    /// <summary>Nivel al que se reduce la pista principal durante el mix (0.0 .. 1.0).</summary>
    public double MixDuckLevel { get; set; } = 0.3;

    /// <summary>Marca pistas virtuales de playlist/bloque que admiten encadenadores.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EncadenadorEligible { get; set; }

    // ───── Propiedades derivadas ─────────────────────────────────────────

    /// <summary>
    /// Título para mostrar:
    ///   • Title custom si está cargado.
    ///   • "Locución: <comando>" si es locución sin título.
    ///   • Nombre del archivo si es audio.
    ///   • "(sin archivo)" en cualquier otro caso.
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;

            if (ItemKind == PlaylistItemKind.Locution)
            {
                var cmdText = LocutionCommand switch
                {
                    LocutionCommand.HourAndMinute => "Hora + minutos",
                    LocutionCommand.Climate => "Clima (temp + humedad)",
                    LocutionCommand.HourAndClimate => "Hora + clima",
                    _ => "Locución"
                };
                return $"Locución: {cmdText}";
            }

            return !string.IsNullOrWhiteSpace(FilePath)
                ? Path.GetFileName(FilePath)
                : "(sin archivo)";
        }
    }

    /// <summary>Duración formateada como hh:mm:ss.</summary>
    [JsonIgnore]
    public string DurationText => AudioFileInfoReader.FormatDuration(DurationSeconds);

    /// <summary>
    /// True si la pista tiene un mixado asignado (audio o locución).
    /// </summary>
    [JsonIgnore]
    public bool HasMix => MixContentType == MixContentType.Locution
        || !string.IsNullOrWhiteSpace(MixAudioPath);
}
