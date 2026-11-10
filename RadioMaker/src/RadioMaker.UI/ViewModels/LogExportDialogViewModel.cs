using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Logging;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del dialogo de exportacion de log.
///
/// Permite elegir:
///   - Formato de salida (TXT / CSV / HTML / XLSX / PDF).
///   - Alcance: todo el log o solo lo filtrado (visible en el panel).
///
/// Cuando el usuario presiona "Exportar...", la ventana usa este VM para
/// abrir el dialogo nativo de guardar y delegar la exportacion a LogExporter.
/// </summary>
public sealed partial class LogExportDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyList<LogEntry> _allEntries;
    private readonly IReadOnlyList<LogEntry> _filteredEntries;

    // ───── Estado observable ─────────────────────────────────────────────

    /// <summary>Formato seleccionado.</summary>
    [ObservableProperty] private LogExportFormat _format = LogExportFormat.Txt;

    /// <summary>True = exportar todo el log. False = solo lo filtrado.</summary>
    [ObservableProperty] private bool _exportAll = true;

    /// <summary>Nombre sugerido para el archivo (sin extension).</summary>
    [ObservableProperty] private string _fileName = string.Empty;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public int AllCount => _allEntries.Count;
    public int FilteredCount => _filteredEntries.Count;

    public string AllLabel => $"Todo el log ({AllCount} entrada(s))";
    public string FilteredLabel => $"Solo lo filtrado ({FilteredCount} entrada(s))";

    public bool HasFiltered => FilteredCount > 0;

    /// <summary>Filtro del dialogo de guardar (ej: "*.txt").</summary>
    public string FilePickerPattern => LogExporter.GetFilePickerPattern(Format);

    /// <summary>Extension final del archivo (sin punto).</summary>
    public string Extension => LogExporter.GetExtension(Format).TrimStart('.');

    /// <summary>Entradas a exportar segun el alcance seleccionado.</summary>
    public IReadOnlyList<LogEntry> EntriesToExport =>
        ExportAll ? _allEntries : _filteredEntries;

    // ───── Formatos disponibles para el ComboBox ─────────────────────────

    public LogExportFormat[] AvailableFormats { get; } =
    {
        LogExportFormat.Txt,
        LogExportFormat.Csv,
        LogExportFormat.Html,
        LogExportFormat.Xlsx,
        LogExportFormat.Pdf
    };

    // ───── Constructor ───────────────────────────────────────────────────

    public LogExportDialogViewModel(
        IReadOnlyList<LogEntry> allEntries,
        IReadOnlyList<LogEntry> filteredEntries)
    {
        _allEntries = allEntries ?? Array.Empty<LogEntry>();
        _filteredEntries = filteredEntries ?? Array.Empty<LogEntry>();

        // Nombre por defecto: rms-log-2026-09-29-1530
        _fileName = $"rms-log-{DateTime.Now:yyyy-MM-dd-HHmm}";
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnFormatChanged(LogExportFormat value)
    {
        OnPropertyChanged(nameof(FilePickerPattern));
        OnPropertyChanged(nameof(Extension));
    }

    partial void OnExportAllChanged(bool value)
    {
        OnPropertyChanged(nameof(EntriesToExport));
    }
}