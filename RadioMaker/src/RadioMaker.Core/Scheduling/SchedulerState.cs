namespace RadioMaker.Core.Scheduling;

/// -------------
/// Estado en tiempo de ejecucion del scheduler.
///
/// Lleva dos registros:
///   1. _lastFired: por cada item, cuando fue disparado la ultima vez.
///   2. _currentlyPlaying: items que estan actualmente sonando o suspendidos.
///
/// El chequeo de "ya disparado" se hace por MINUTO CALENDARIO, no por
/// ventana deslizante: un item no puede dispararse dos veces en el mismo
/// minuto ni en el minuto siguiente. Esto se debe a que el evaluador tiene
/// una tolerancia de +1 minuto (por si el tick del scheduler cae justo
/// despues del cambio de minuto), y esa tolerancia hacia que un item
/// programado para 14:30 se disparara a las 14:30 y tambien a las 14:31.
/// -------------
public sealed class SchedulerState
{
    private readonly Dictionary<string, DateTime> _lastFired = new();
    private readonly HashSet<string> _currentlyPlaying = new();

    // ───── Registro de disparos ──────────────────────────────────────────

    /// -------------Registra que un item se disparo ahora.</summary>
    public void MarkFired(string itemId, DateTime firedAt)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        _lastFired[itemId] = firedAt;
    }

    /// -------------
    /// Devuelve true si el item ya se disparo en el minuto actual O en el
    /// minuto inmediatamente anterior.
    ///
    /// Ejemplos (item disparado a las 14:30:00):
    ///   14:30:xx → true   (mismo minuto)
    ///   14:31:xx → true   (minuto siguiente, tolerancia)
    ///   14:32:xx → false  (ya pasaron 2 minutos)
    ///
    /// Este bloqueo es lo que impide que un item se dispare dos veces por
    /// la tolerancia de +1 minuto del ScheduleEvaluator.
    /// -------------
    public bool AlreadyFiredThisOrPreviousMinute(string itemId, DateTime now)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        if (!_lastFired.TryGetValue(itemId, out var lastFired)) return false;

        // Truncar ambos a minuto exacto.
        var lastMinute = new DateTime(
            lastFired.Year, lastFired.Month, lastFired.Day,
            lastFired.Hour, lastFired.Minute, 0);

        var nowMinute = new DateTime(
            now.Year, now.Month, now.Day,
            now.Hour, now.Minute, 0);

        double diffMinutes = (nowMinute - lastMinute).TotalMinutes;

        // Bloquear si estamos en el mismo minuto o en el inmediato siguiente.
        return diffMinutes >= 0 && diffMinutes <= 1;
    }

    // ───── Registro de "actualmente en reproduccion" ─────────────────────

    /// -------------Marca un item como actualmente en reproduccion (sonando o suspendido).</summary>
    public void MarkPlaying(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        lock (_currentlyPlaying) _currentlyPlaying.Add(itemId);
    }

    /// -------------Desmarca un item cuando termina (definitivamente) o se cancela.</summary>
    public void UnmarkPlaying(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        lock (_currentlyPlaying) _currentlyPlaying.Remove(itemId);
    }

    /// -------------Devuelve true si el item esta actualmente sonando o suspendido.</summary>
    public bool IsPlaying(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        lock (_currentlyPlaying) return _currentlyPlaying.Contains(itemId);
    }

    // ───── Limpieza ──────────────────────────────────────────────────────

    public void Clear()
    {
        _lastFired.Clear();
        lock (_currentlyPlaying) _currentlyPlaying.Clear();
    }
}