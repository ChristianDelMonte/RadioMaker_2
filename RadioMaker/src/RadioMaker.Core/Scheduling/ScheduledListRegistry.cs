namespace RadioMaker.Core.Scheduling;

/// -------------
/// Registro de todas las ScheduledList "activas" en la aplicacion.
///
/// Una lista se registra cuando el usuario abre/crea una pestaña de programados
/// y se desregistra cuando cierra la pestaña. El motor de programacion
/// (Sprint 6.5.2) consulta este registro para saber que items debe evaluar.
///
/// Es thread-safe: usa lock simple porque el scheduler corre en otro hilo.
/// -------------
public sealed class ScheduledListRegistry
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();

    /// -------------Entrada de registro: la lista + un nombre para mostrar en logs.</summary>
    private sealed class Entry
    {
        public string Name { get; init; } = string.Empty;
        public ScheduledList List { get; init; } = new();
        public bool IsEnabled { get; set; } = true;
    }

    /// -------------Se dispara cuando se agrega, quita o modifica una lista.</summary>
    public event EventHandler? Changed;

    // ───── Registro ──────────────────────────────────────────────────────

    /// -------------Registra una lista. Es idempotente (si ya estaba, la ignora).</summary>
    public void Register(ScheduledList list, string displayName)
    {
        if (list is null) return;

        lock (_gate)
        {
            if (_entries.Any(e => ReferenceEquals(e.List, list))) return;

            _entries.Add(new Entry
            {
                List = list,
                Name = string.IsNullOrWhiteSpace(displayName) ? "Sin nombre" : displayName,
                // Respetar el flag persistido en el .rms.
                IsEnabled = list.IsEnabled
            });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// -------------Desregistra una lista.</summary>
    public void Unregister(ScheduledList list)
    {
        if (list is null) return;

        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => ReferenceEquals(e.List, list)) > 0;
        }

        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// -------------Habilita o deshabilita una lista del scheduler sin desregistrarla.</summary>
    public void SetEnabled(ScheduledList list, bool enabled)
    {
        if (list is null) return;

        bool changed = false;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => ReferenceEquals(e.List, list));
            if (entry is not null && entry.IsEnabled != enabled)
            {
                entry.IsEnabled = enabled;
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// -------------Indica si una lista esta registrada y habilitada.</summary>
    public bool IsEnabled(ScheduledList list)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => ReferenceEquals(e.List, list));
            return entry?.IsEnabled ?? false;
        }
    }

    // ───── Consulta ──────────────────────────────────────────────────────

    /// -------------Devuelve una copia de todas las listas registradas (haya o no habilitadas).</summary>
    public IReadOnlyList<ScheduledList> GetAll()
    {
        lock (_gate)
        {
            return _entries.Select(e => e.List).ToList();
        }
    }

    /// -------------Devuelve solo las listas registradas Y habilitadas.</summary>
    public IReadOnlyList<ScheduledList> GetEnabled()
    {
        lock (_gate)
        {
            return _entries.Where(e => e.IsEnabled).Select(e => e.List).ToList();
        }
    }

    /// -------------Devuelve cuantas listas hay registradas.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// -------------Devuelve cuantas listas estan habilitadas.</summary>
    public int EnabledCount
    {
        get
        {
            lock (_gate) return _entries.Count(e => e.IsEnabled);
        }
    }

    /// -------------Limpia todo el registro.</summary>
    public void Clear()
    {
        bool wasEmpty;
        lock (_gate)
        {
            wasEmpty = _entries.Count == 0;
            _entries.Clear();
        }

        if (!wasEmpty)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}