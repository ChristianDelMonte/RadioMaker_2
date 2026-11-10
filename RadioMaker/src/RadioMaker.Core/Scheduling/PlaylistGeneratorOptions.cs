using System.Text.Json.Serialization;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Modo de armado: por cantidad de pistas o por duracion total.
/// </summary>
public enum PlaylistGeneratorMode
{
    TrackCount = 0,
    TotalDuration = 1
}

/// <summary>
/// Orden en que se van eligiendo las pistas candidatas antes de armar
/// la seleccion final.
/// </summary>
public enum PlaylistGeneratorOrder
{
    Random = 0,
    AlphabeticalByName = 1,
    AlphabeticalByPath = 2
}

/// <summary>
/// Configuracion del generador automatico de playlists.
///
/// Este modelo es serializable a JSON y se va a usar tambien para
/// guardar/cargar presets .rpg en una fase posterior.
/// </summary>
public sealed class PlaylistGeneratorOptions
{
    public int Version { get; set; } = 1;

    /// <summary>Carpeta de origen donde buscar los archivos de audio.</summary>
    public string SourceFolder { get; set; } = string.Empty;

    /// <summary>Si true, se incluyen subcarpetas recursivamente.</summary>
    public bool IncludeSubfolders { get; set; } = false;

    /// <summary>Modo de armado (cantidad o duracion).</summary>
    public PlaylistGeneratorMode Mode { get; set; } = PlaylistGeneratorMode.TotalDuration;

    /// <summary>Cantidad de pistas objetivo (solo modo TrackCount).</summary>
    public int TargetTrackCount { get; set; } = 20;

    /// <summary>Duracion objetivo en minutos (solo modo TotalDuration).</summary>
    public double TargetDurationMinutes { get; set; } = 60.0;

    /// <summary>
    /// Tolerancia en minutos respecto al objetivo. El algoritmo intenta
    /// que la suma quede dentro de [target - tolerance, target + tolerance].
    /// </summary>
    public double ToleranceMinutes { get; set; } = 3.0;

    /// <summary>Orden en que se van tomando las pistas.</summary>
    public PlaylistGeneratorOrder Order { get; set; } = PlaylistGeneratorOrder.Random;

    // ───── Filtros por tags (Fase 2) ─────────────────────────────────────

    public string FilterGenre { get; set; } = string.Empty;
    public string FilterArtist { get; set; } = string.Empty;
    public string FilterAlbum { get; set; } = string.Empty;

    public bool IsYearRangeFilterEnabled { get; set; }
    public int FilterYearMin { get; set; } = 1900;
    public int FilterYearMax { get; set; } = DateTime.Now.Year + 1;

    public bool IsDurationRangeFilterEnabled { get; set; }
    public double FilterMinDurationMinutes { get; set; } = 0.5;
    public double FilterMaxDurationMinutes { get; set; } = 10.0;

    // ───── Reglas de variedad (Fase 3) ───────────────────────────────────

    /// <summary>
    /// Si true, no permite dos pistas consecutivas del mismo artista.
    /// Es un caso especial de <see cref="MinTracksBetweenSameArtist"/>
    /// con ventana = 1. Se expone por separado para que la UI lo muestre
    /// como un checkbox independiente (mas claro que un numero).
    /// </summary>
    public bool AvoidSameArtistConsecutive { get; set; }

    /// <summary>
    /// Cantidad minima de pistas que deben pasar antes de repetir el mismo
    /// artista. 0 = sin restriccion. Si <see cref="AvoidSameArtistConsecutive"/>
    /// tambien esta activo, se toma el MAXIMO entre ambos (ventana = max(1, N)).
    /// </summary>
    public int MinTracksBetweenSameArtist { get; set; }

    /// <summary>
    /// Si true, reordena el pool con round-robin por artista antes de
    /// seleccionar. Reparte los temas de cada artista de forma equitativa
    /// a lo largo de toda la playlist.
    /// </summary>
    public bool BalanceByArtist { get; set; }

    /// <summary>
    /// Si true, cuando las reglas de variedad impiden llegar al objetivo
    /// de cantidad/duracion, se hace una segunda pasada relajando esas
    /// reglas para completar. Esto puede romper la variedad pero asegura
    /// que la playlist no quede corta.
    /// </summary>
    public bool RelaxIfNotEnough { get; set; } = true;

    // ───── Propiedades calculadas ────────────────────────────────────────

    /// <summary>
    /// True si hay al menos un filtro que requiera LEER TAGS de los archivos.
    /// Cuando es false, el servicio se saltea la lectura de tags por
    /// completo (mucho mas rapido en carpetas grandes).
    ///
    /// Nota: las reglas de variedad por artista tambien requieren leer
    /// el tag de artista, asi que tambien activan la lectura de tags.
    /// </summary>
    [JsonIgnore]
    public bool HasActiveTagFilters =>
        !string.IsNullOrWhiteSpace(FilterGenre)
        || !string.IsNullOrWhiteSpace(FilterArtist)
        || !string.IsNullOrWhiteSpace(FilterAlbum)
        || IsYearRangeFilterEnabled
        || HasActiveVarietyRules;

    /// <summary>
    /// True si hay alguna regla de variedad que dependa del tag de artista.
    /// </summary>
    [JsonIgnore]
    public bool HasActiveVarietyRules =>
        AvoidSameArtistConsecutive
        || MinTracksBetweenSameArtist > 0
        || BalanceByArtist;

    /// <summary>
    /// True si hay al menos un filtro activo (tag, duracion o variedad).
    /// </summary>
    [JsonIgnore]
    public bool HasAnyFilter =>
        HasActiveTagFilters || IsDurationRangeFilterEnabled;

    /// <summary>
    /// Tamaño efectivo de la ventana deslizante de artistas.
    /// 0 = sin restriccion de ventana.
    /// </summary>
    [JsonIgnore]
    public int EffectiveArtistWindow
    {
        get
        {
            int w = Math.Max(0, MinTracksBetweenSameArtist);
            if (AvoidSameArtistConsecutive) w = Math.Max(1, w);
            return w;
        }
    }
}