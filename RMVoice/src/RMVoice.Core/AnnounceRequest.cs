namespace RMVoice.Core;

/// <summary>
/// Solicitud de una locución.
/// RadioMaker construye esto y se lo pasa a <see cref="IAnnouncer.Play"/>.
/// </summary>
public sealed class AnnounceRequest
{
    /// <summary>Qué piezas anunciar. Por defecto: todo.</summary>
    public AnnounceContent Content { get; init; } = AnnounceContent.HourMinuteAndClimate;

    /// <summary>
    /// Hora a anunciar (0–23). Si es <c>null</c>, el Engine usa la hora actual del sistema.
    /// </summary>
    public int? Hour { get; init; }

    /// <summary>
    /// Minuto a anunciar (0–59). Si es <c>null</c>, el Engine usa el minuto actual del sistema.
    /// </summary>
    public int? Minute { get; init; }

    /// <summary>
    /// Temperatura en °C. Se recorta a [-30, 50] antes de buscar la región.
    /// Si es <c>null</c>, el Engine usa la última lectura de clima cacheada.
    /// </summary>
    public int? Temperature { get; init; }

    /// <summary>
    /// Humedad relativa en %. Se recorta a [5, 100] antes de buscar la región.
    /// Si es <c>null</c>, el Engine usa la última lectura de clima cacheada.
    /// </summary>
    public int? Humidity { get; init; }

    /// <summary>
    /// Índice de dispositivo BASS donde reproducir.
    /// <c>-1</c> (por defecto) = dispositivo por defecto del sistema.
    /// </summary>
    public int DeviceIndex { get; init; } = -1;

    /// <summary>Volumen de la locución, 0.0 – 1.0.</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>
    /// Si <c>true</c>, y el Engine ya está reproduciendo otra locución,
    /// lanza <see cref="InvalidOperationException"/> en lugar de esperar.
    /// Por defecto es <c>false</c>: la llamada espera su turno.
    /// </summary>
    public bool ThrowIfBusy { get; init; }

    /// <summary>
    /// Texto legible que describe el contenido del request.
    /// Útil para mostrar en UI ("Hora + clima") o para logging.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (Content == AnnounceContent.None)
                return "(nada)";

            // Atajos con nombre bonito
            if (Content == AnnounceContent.HourOnly) return "Hora";
            if (Content == AnnounceContent.HourAndMinute) return "Hora + minutos";
            if (Content == AnnounceContent.ClimateOnly) return "Clima";
            if (Content == AnnounceContent.HourMinuteAndClimate) return "Hora + clima";

            // Combinaciones libres: construimos la cadena parte por parte
            var parts = new List<string>();
            if (Content.HasFlag(AnnounceContent.Hour)) parts.Add("Hora");
            if (Content.HasFlag(AnnounceContent.Minute)) parts.Add("Minutos");
            if (Content.HasFlag(AnnounceContent.Temperature)) parts.Add("Temperatura");
            if (Content.HasFlag(AnnounceContent.Humidity)) parts.Add("Humedad");

            return parts.Count == 0 ? "(nada)" : string.Join(" + ", parts);
        }
    }
}