namespace RadioMaker.Core.Scheduling;

/// <summary>Coordina la única lista activa y el ritmo de inserción de encadenadores.</summary>
public sealed class EncadenadoresRegistry
{
    private readonly object _gate = new();
    private EncadenadoresSet? _activeSet;
    private string? _activeOwnerId;
    private string? _lastSourceId;
    private int _audioCount;
    private int _sequentialIndex;
    private List<string> _randomCycle = new();
    private int _randomIndex;

    public event EventHandler? ActiveSetChanged;

    public string? ActiveOwnerId
    {
        get { lock (_gate) return _activeOwnerId; }
    }

    public bool IsActive(string ownerId)
    {
        lock (_gate) return string.Equals(ownerId, _activeOwnerId, StringComparison.Ordinal);
    }

    public void Activate(string ownerId, EncadenadoresSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        lock (_gate)
        {
            if (_activeSet is not null) _activeSet.IsActive = false;
            _activeOwnerId = ownerId;
            _activeSet = set;
            _activeSet.IsActive = true;
            ResetPlaybackState();
        }
        ActiveSetChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate(string ownerId)
    {
        lock (_gate)
        {
            if (!string.Equals(ownerId, _activeOwnerId, StringComparison.Ordinal)) return;
            if (_activeSet is not null) _activeSet.IsActive = false;
            _activeSet = null;
            _activeOwnerId = null;
            ResetPlaybackState();
        }
        ActiveSetChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(string ownerId, EncadenadoresSet set)
    {
        lock (_gate)
        {
            if (!string.Equals(ownerId, _activeOwnerId, StringComparison.Ordinal)) return;
            _activeSet = set;
            set.IsActive = true;
            ResetPlaybackState();
        }
    }

    /// <summary>
    /// Cuenta un audio elegible y devuelve el siguiente encadenador si toca
    /// insertarlo antes de otro audio elegible.
    /// </summary>
    public bool TryTakeAfterAudio(string sourceId, out EncadenadorTrack? track)
    {
        lock (_gate)
        {
            track = null;
            var set = _activeSet;
            if (set is null) return false;

            var available = set.Tracks
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
                .ToList();
            if (available.Count == 0) return false;

            set.InsertEvery = Math.Clamp(set.InsertEvery, 1, 3);
            if (set.CounterReset == EncadenadorCounterReset.OnSourceChange &&
                !string.Equals(_lastSourceId, sourceId, StringComparison.Ordinal))
                _audioCount = 0;
            _lastSourceId = sourceId;

            _audioCount++;
            if (_audioCount < set.InsertEvery) return false;
            _audioCount = 0;

            if (set.Order == EncadenadorOrder.Sequential)
            {
                _sequentialIndex %= available.Count;
                track = available[_sequentialIndex++];
                return true;
            }

            if (_randomCycle.Count != available.Count ||
                !_randomCycle.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                    available.Select(t => t.Id).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                _randomCycle = available.Select(t => t.Id).OrderBy(_ => Random.Shared.Next()).ToList();
                _randomIndex = 0;
            }
            else if (_randomIndex >= _randomCycle.Count)
            {
                _randomCycle = available.Select(t => t.Id).OrderBy(_ => Random.Shared.Next()).ToList();
                _randomIndex = 0;
            }

            var nextId = _randomCycle[_randomIndex++];
            track = available.First(t => t.Id == nextId);
            return true;
        }
    }

    /// <summary>Consulta el encadenador que tocaría después del audio actual sin avanzar el contador.</summary>
    public bool TryPeekAfterAudio(string sourceId, out EncadenadorTrack? track)
    {
        lock (_gate)
        {
            track = null;
            var set = _activeSet;
            if (set is null) return false;

            var available = set.Tracks
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
                .ToList();
            if (available.Count == 0) return false;

            var count = set.CounterReset == EncadenadorCounterReset.OnSourceChange &&
                        !string.Equals(_lastSourceId, sourceId, StringComparison.Ordinal)
                ? 0
                : _audioCount;
            set.InsertEvery = Math.Clamp(set.InsertEvery, 1, 3);
            if (count + 1 < set.InsertEvery) return false;

            if (set.Order == EncadenadorOrder.Sequential)
            {
                track = available[_sequentialIndex % available.Count];
                return true;
            }

            if (_randomCycle.Count != available.Count ||
                !_randomCycle.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                    available.Select(t => t.Id).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                _randomCycle = available.Select(t => t.Id).OrderBy(_ => Random.Shared.Next()).ToList();
                _randomIndex = 0;
            }
            else if (_randomIndex >= _randomCycle.Count)
            {
                _randomCycle = available.Select(t => t.Id).OrderBy(_ => Random.Shared.Next()).ToList();
                _randomIndex = 0;
            }

            var nextId = _randomCycle[_randomIndex];
            track = available.First(t => t.Id == nextId);
            return true;
        }
    }

    private void ResetPlaybackState()
    {
        _lastSourceId = null;
        _audioCount = 0;
        _sequentialIndex = 0;
        _randomCycle.Clear();
        _randomIndex = 0;
    }
}
