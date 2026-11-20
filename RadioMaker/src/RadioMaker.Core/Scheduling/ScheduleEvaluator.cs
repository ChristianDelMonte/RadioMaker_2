namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Evaluador de horarios: determina si un ScheduledItem debe dispararse
/// en un momento dado.
///
/// Reglas:
///   1. El item debe estar habilitado (IsEnabled = true).
///   2. Debe tener al menos una entrada de horario habilitada.
///   3. Alguna de esas entradas debe matchear el momento actual:
///      - El instante actual debe estar dentro de los 2 minutos desde la
///        hora programada, incluyendo el cruce de medianoche.
///
/// Tolerancia:
///   El scheduler corre cada N segundos. Para no perder disparos si el
///   reloj avanza justo entre 2 ticks, se admite una ventana de dos minutos
///   desde la hora programada, también al cruzar medianoche.
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

        foreach (var entry in item.Entries)
        {
            if (entry is null) continue;
            if (!entry.IsEnabled) continue;
            if (entry.Days is null || entry.Days.Count == 0) continue;
            if (entry.Hour is < 0 or > 23 || entry.Minute is < 0 or > 59) continue;

            // Evaluar la hora programada como un instante completo. Incluir
            // el día anterior permite conservar el minuto de tolerancia al
            // cruzar medianoche (p. ej. 23:59 → tick a las 00:00).
            var scheduledToday = now.Date.AddHours(entry.Hour).AddMinutes(entry.Minute);
            var scheduledYesterday = scheduledToday.AddDays(-1);

            if (entry.Days.Contains(scheduledToday.DayOfWeek) &&
                now >= scheduledToday && now < scheduledToday.AddMinutes(2))
                return true;

            if (entry.Days.Contains(scheduledYesterday.DayOfWeek) &&
                now >= scheduledYesterday && now < scheduledYesterday.AddMinutes(2))
                return true;
        }

        return false;
    }
}
