using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Logging;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del panel de log de reproduccion.
///
/// Muestra en vivo las entradas emitidas por el LogService, con:
///   - Filtro por nivel (Debug / Info / Warning / Error).
///   - Filtro por categoria (texto libre).
///   - Contador por nivel.
///   - Boton para limpiar.
///   - Boton para exportar (via dialogo modal).
/// </summary>
public sealed partial class PlaybackLogViewModel : ViewModelBase
{
    private readonly LogService _logService;
    private const int MaxVisible = 500;

    // ───── Colecciones ───────────────────────────────────────────────────

    /// <summary>Entradas visibles en el panel (aplicando los filtros activos).</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    // ───── Filtros ───────────────────────────────────────────────────────

    [ObservableProperty] private bool _showDebug = true;
    [ObservableProperty] private bool _showInfo = true;
    [ObservableProperty] private bool _showWarning = true;
    [ObservableProperty] private bool _showError = true;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;

    // ───── Contadores ────────────────────────────────────────────────────

    [ObservableProperty] private int _debugCount;
    [ObservableProperty] private int _infoCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _errorCount;

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaybackLogViewModel(LogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));

        _logService.EntryAdded += OnEntryAdded;

        ReloadSnapshot();
    }

    // ───── API para exportacion ──────────────────────────────────────────

    /// <summary>
    /// Devuelve TODAS las entradas del LogService (sin aplicar filtros),
    /// ordenadas de mas reciente a mas antigua.
    /// Se usa desde el dialogo de exportacion cuando se elige "todo el log".
    /// </summary>
    public IReadOnlyList<LogEntry> GetAllEntries()
    {
        return _logService.GetSnapshot(int.MaxValue);
    }

    /// <summary>
    /// Devuelve solo las entradas filtradas actualmente (las visibles en el panel).
    /// Se usa desde el dialogo de exportacion cuando se elige "solo lo filtrado".
    /// </summary>
    public IReadOnlyList<LogEntry> GetFilteredEntries()
    {
        return Entries.ToList();
    }

    // ───── Handlers ──────────────────────────────────────────────────────

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IncrementCount(entry.Level);

            if (PassesFilter(entry))
            {
                Entries.Insert(0, entry);

                while (Entries.Count > MaxVisible)
                    Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }

    private void IncrementCount(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Debug: DebugCount++; break;
            case LogLevel.Info: InfoCount++; break;
            case LogLevel.Warning: WarningCount++; break;
            case LogLevel.Error: ErrorCount++; break;
        }
    }

    private bool PassesFilter(LogEntry entry)
    {
        switch (entry.Level)
        {
            case LogLevel.Debug: if (!ShowDebug) return false; break;
            case LogLevel.Info: if (!ShowInfo) return false; break;
            case LogLevel.Warning: if (!ShowWarning) return false; break;
            case LogLevel.Error: if (!ShowError) return false; break;
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var needle = FilterText.Trim();
            bool matches =
                entry.Message.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                entry.Category.Contains(needle, StringComparison.OrdinalIgnoreCase);

            if (!matches) return false;
        }

        return true;
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearLog()
    {
        _logService.Clear();
        Entries.Clear();
        DebugCount = 0;
        InfoCount = 0;
        WarningCount = 0;
        ErrorCount = 0;
    }

    [RelayCommand]
    private void ReloadSnapshot()
    {
        var snapshot = _logService.GetSnapshot(MaxVisible);

        Entries.Clear();
        foreach (var entry in snapshot)
        {
            if (PassesFilter(entry))
                Entries.Add(entry);
        }

        DebugCount = _logService.CountByLevel(LogLevel.Debug);
        InfoCount = _logService.CountByLevel(LogLevel.Info);
        WarningCount = _logService.CountByLevel(LogLevel.Warning);
        ErrorCount = _logService.CountByLevel(LogLevel.Error);
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnShowDebugChanged(bool value) => ReloadSnapshot();
    partial void OnShowInfoChanged(bool value) => ReloadSnapshot();
    partial void OnShowWarningChanged(bool value) => ReloadSnapshot();
    partial void OnShowErrorChanged(bool value) => ReloadSnapshot();
    partial void OnFilterTextChanged(string value) => ReloadSnapshot();
}