namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Una entrada de horario dentro de un item programado.
/// Un item puede tener varias entradas (ej: lunes 9:00 + miércoles 14:00).
/// </summary>
public sealed class ScheduleEntry
{
    /// <summary>Dias de la semana en los que aplica esta entrada.</summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary>Hora del dia (0-23).</summary>
    public int Hour { get; set; }

    /// <summary>Minuto (0-59).</summary>
    public int Minute { get; set; }

    /// <summary>Si la entrada esta activa. Permite deshabilitarla sin borrarla.</summary>
    public bool IsEnabled { get; set; } = true;
}