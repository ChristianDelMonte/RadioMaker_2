using System.Threading;

namespace RadioMaker.Core.Scheduling;

/// <summary>
/// Servicio que corre en un timer en background, evalua periodicamente
/// todos los items programados y dispara el evento ItemTriggered cuando
/// corresponde.
///
/// Flujo:
///   1. Un timer dispara OnTick cada TickIntervalMs.
///   2. OnTick toma la hora local actual, recorre las listas habilitadas
///      y evalua cada item con ScheduleEvaluator.
///   3. Si un item debe dispararse, NO esta ya reproduciendose, y no se
///      disparo recientemente, emite ItemTriggered + log.
/// </summary>
public sealed class SchedulerService : IDisposable
{
    public const int DefaultTickIntervalMs = 5000;

    private readonly ScheduledListRegistry _registry;
    private readonly SchedulerState _state = new();
    private readonly object _logGate = new();
    private readonly List<SchedulerLogEntry> _log = new();
    private const int MaxLogEntries = 500;

    private Timer? _timer;
    private bool _isRunning;
    private bool _disposed;

    // ───── Eventos ───────────────────────────────────────────────────────

    public event EventHandler<SchedulerEventArgs>? ItemTriggered;
    public event EventHandler<SchedulerLogEntry>? LogAdded;
    public event EventHandler? StateChanged;
    /// <summary>Se dispara cuando se reinicia el estado de disparos.</summary>
    public event EventHandler? StateReset;
    // ───── Propiedades publicas ──────────────────────────────────────────

    public bool IsRunning => _isRunning;
    public ScheduledListRegistry Registry => _registry;
    public SchedulerState State => _state;

    public IReadOnlyList<SchedulerLogEntry> GetLogSnapshot()
    {
        lock (_logGate)
        {
            return _log.OrderByDescending(e => e.Timestamp).ToList();
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public SchedulerService(ScheduledListRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registry.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ───── Ciclo de vida ─────────────────────────────────────────────────

    public void Start()
    {
        if (_disposed) return;
        if (_isRunning) return;

        _timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(DefaultTickIntervalMs));
        _isRunning = true;

        AddLog(SchedulerLogLevel.Info, "Scheduler iniciado.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _timer?.Dispose();
        _timer = null;
        _isRunning = false;

        AddLog(SchedulerLogLevel.Info, "Scheduler detenido.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ───── Ciclo de evaluacion ───────────────────────────────────────────

    private void OnTick(object? state)
    {
        if (_disposed || !_isRunning) return;

        try
        {
            var now = DateTime.Now;
            var lists = _registry.GetEnabled();

            if (lists.Count == 0) return;

            foreach (var list in lists)
            {
                if (list.Items is null) continue;

                foreach (var item in list.Items)
                {
                    if (item is null) continue;
                    if (string.IsNullOrEmpty(item.Id)) continue;

                    // ¿Ya esta reproduciendose o suspendido?
                    // (Este es el chequeo clave que evita el re-disparo
                    //  del item Baja cuando se reanuda.)
                    if (_state.IsPlaying(item.Id))
                        continue;

                    if (!ScheduleEvaluator.ShouldFire(item, now))
                        continue;

                    if (_state.AlreadyFiredThisOrPreviousMinute(item.Id, now))
                        continue;

                    _state.MarkFired(item.Id, now);

                    var args = new SchedulerEventArgs
                    {
                        Item = item,
                        SourceListName = list.DisplayName,
                        FiredAt = now
                    };

                    AddLog(SchedulerLogLevel.Trigger,
                        $"Disparando: {item.DisplayTitle} ({item.Priority}) desde '{list.DisplayName}'");

                    RaiseItemTriggered(args);
                }
            }
        }
        catch (Exception ex)
        {
            AddLog(SchedulerLogLevel.Error, $"Error en tick: {ex.Message}");
        }
    }

    private void RaiseItemTriggered(SchedulerEventArgs args)
    {
        try
        {
            ItemTriggered?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            AddLog(SchedulerLogLevel.Error, $"Error en handler de ItemTriggered: {ex.Message}");
        }
    }

    // ───── Log ───────────────────────────────────────────────────────────

    public void AddLog(SchedulerLogLevel level, string message)
    {
        var entry = new SchedulerLogEntry(DateTime.Now, level, message);

        lock (_logGate)
        {
            _log.Add(entry);
            if (_log.Count > MaxLogEntries)
                _log.RemoveRange(0, _log.Count - MaxLogEntries);
        }

        try { LogAdded?.Invoke(this, entry); } catch { }
    }

    // ───── API publica ───────────────────────────────────────────────────

    /// <summary>Limpia el estado de disparo (util si el usuario quiere rearmar).</summary>
    public void ResetState()
    {
        _state.Clear();
        StateReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fuerza la evaluacion inmediata sin esperar al siguiente tick.</summary>
    public void EvaluateNow() => OnTick(null);
}