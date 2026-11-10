namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Evaluador de horarios: determina si un ScheduledItem debe dispararse
/// en un momento dado.
///
/// Reglas:
///   1. El item debe estar habilitado (IsEnabled = true).
///   2. Debe tener al menos una entrada de horario habilitada.
///   3. Alguna de esas entradas debe matchear el momento actual:
///      - El dia de la semana actual debe estar en entry.Days.
///      - La hora actual debe ser entry.Hour.
///      - El minuto actual debe ser entry.Minute (o estar dentro de la
///        ventana de tolerancia, ver mas abajo).
///
/// Tolerancia:
///   El scheduler corre cada N segundos. Para no perder disparos si el
///   reloj avanza justo entre 2 ticks, permitimos que el minuto actual
///   sea igual al minuto configurado O UN minuto despues (por si el
///   disparo programado fue a las 14:30:00 y el tick cayo a las 14:31:00).
///   El SchedulerState se encarga de evitar disparos duplicados.
/// </summary>
public static class ScheduleEvaluator
{
    /// <summary>
    /// Devuelve true si el item debe dispararse en <paramref name="now"/>.
    ///
    /// <paramref name="now"/> es la hora LOCAL (no UTC) porque los horarios
    /// programados por el usuario se interpretan en su zona horaria.
    /// </summary>
    public static bool ShouldFire(ScheduledItem item, DateTime now)
    {
        if (item is null) return false;
        if (!item.IsEnabled) return false;
        if (item.Entries is null || item.Entries.Count == 0) return false;

        var today = now.DayOfWeek;

        foreach (var entry in item.Entries)
        {
            if (entry is null) continue;
            if (!entry.IsEnabled) continue;

            // Chequear dia de la semana.
            if (entry.Days is null || !entry.Days.Contains(today))
                continue;

            // Chequear hora.
            if (entry.Hour != now.Hour)
                continue;

            // Chequear minuto con tolerancia de 1 minuto hacia adelante.
            // Ejemplo: si la entrada es 14:30 y el reloj marca 14:30:45,
            // matchea. Si marca 14:31:12, tambien matchea (tolerancia).
            // Si marca 14:32, ya no.
            int diffMinutes = now.Minute - entry.Minute;
            if (diffMinutes == 0 || diffMinutes == 1)
                return true;
        }

        return false;
    }
}