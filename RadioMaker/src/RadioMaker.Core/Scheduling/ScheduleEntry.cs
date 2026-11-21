namespace RadioMaker.Core.Scheduling;

/// -------------
/// Una entrada de horario dentro de un item programado.
/// Un item puede tener varias entradas (ej: lunes 9:00 + miércoles 14:00).
/// -------------
public sealed class ScheduleEntry
{
    /// -------------Dias de la semana en los que aplica esta entrada.</summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// -------------Hora del dia (0-23).</summary>
    public int Hour { get; set; }

    /// -------------Minuto (0-59).</summary>
    public int Minute { get; set; }

    /// -------------Si la entrada esta activa. Permite deshabilitarla sin borrarla.</summary>
    public bool IsEnabled { get; set; } = true;
}