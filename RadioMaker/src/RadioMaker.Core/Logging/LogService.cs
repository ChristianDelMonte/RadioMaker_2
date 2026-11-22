using System.Diagnostics;
using System.Threading.Channels;

namespace RadioMaker.Core.Logging;

/// -------------
/// Servicio central de log de la aplicacion.
///
/// Los modulos (AudioCore, UI, scheduler, reproductores) escriben entradas
/// a traves de este servicio. La UI se suscribe al evento EntryAdded para
/// mostrar el log en vivo.
///
/// Ademas mantiene las entradas en memoria (para el panel de log), las
/// persiste automaticamente a disco:
///   - Un archivo por dia en la carpeta de datos de usuario de RadioMaker.
///   - Nombre: rms-log-YYYY-MM-DD.log
///   - Si la carpeta no existe, se crea automaticamente.
///   - Cuando cambia el dia (app corriendo mas alla de la medianoche),
///     se abre un archivo nuevo sin reiniciar la app.
///
/// Thread-safe: los metodos se pueden llamar desde cualquier hilo
/// (incluyendo los hilos internos de BASS). La escritura a disco se encola
/// para no bloquear callbacks de audio.
/// -------------
public sealed class LogService : IDisposable
{
    private const int MaxEntries = 2000;

    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = new();
    private readonly Channel<LogEntry> _fileQueue = Channel.CreateBounded<LogEntry>(
        new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly Task _fileWriterTask;

    // ───── Persistencia a archivo ────────────────────────────────────────

    private readonly object _fileGate = new();
    private StreamWriter? _fileWriter;
    private string? _currentLogDate;

    /// -------------Carpeta donde se guardan los archivos de log.</summary>
    public static string LogFolder { get; } = BuildLogFolderPath();

    /// -------------Ruta completa del archivo de log actual (o null si aun no se creo).</summary>
    public string? CurrentLogFilePath { get; private set; }

    /// -------------Se dispara cuando se agrega una nueva entrada.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    public LogService()
    {
        _fileWriterTask = Task.Run(ProcessFileQueueAsync);
    }

    // ───── Escritura ─────────────────────────────────────────────────────

    public void Debug(string category, string message) => Write(LogLevel.Debug, category, message);
    public void Info(string category, string message) => Write(LogLevel.Info, category, message);
    public void Warning(string category, string message) => Write(LogLevel.Warning, category, message);
    public void Warning(string category, string message, Exception ex)
        => Write(LogLevel.Warning, category, FormatException(message, ex));
    public void Error(string category, string message) => Write(LogLevel.Error, category, message);
    public void Error(string category, string message, Exception ex)
        => Write(LogLevel.Error, category, FormatException(message, ex));

    private static string FormatException(string message, Exception ex)
        => $"{message}: {ex}".Replace("\r", " ").Replace("\n", " | ");

    public void Write(LogLevel level, string category, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, category ?? string.Empty, message ?? string.Empty);

        // 1) Agregar a la lista en memoria (para el panel de UI).
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        // 2) Encolar la persistencia para no hacer I/O en el hilo llamador.
        if (!_fileQueue.Writer.TryWrite(entry))
            Trace.TraceError("No se pudo encolar entrada de log [{0}] {1}", category, message);

        // 3) Notificar a los suscriptores (UI).
        try { EntryAdded?.Invoke(this, entry); } catch { }
    }

    // ───── Lectura ───────────────────────────────────────────────────────

    /// -------------Snapshot de las ultimas N entradas (mas recientes primero).</summary>
    public IReadOnlyList<LogEntry> GetSnapshot(int max = 500)
    {
        lock (_gate)
        {
            return _entries
                .TakeLast(max)
                .Reverse()
                .ToList();
        }
    }

    /// -------------Cuenta de entradas por nivel.</summary>
    public int CountByLevel(LogLevel level)
    {
        lock (_gate) return _entries.Count(e => e.Level == level);
    }

    /// -------------Limpia todo el log en memoria (NO borra el archivo en disco).</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    // ───── Persistencia ──────────────────────────────────────────────────

    /// -------------
    /// Escribe una entrada al archivo del dia. Si es la primera escritura
    /// del dia o si cambio la fecha (medianoche), rota el archivo.
    /// -------------
    private void WriteToFile(LogEntry entry)
    {
        lock (_fileGate)
        {
            try
            {
                var today = entry.Timestamp.ToString("yyyy-MM-dd");

                // Rotar si cambio el dia o si no hay writer todavia.
                if (_fileWriter is null || !string.Equals(_currentLogDate, today, StringComparison.Ordinal))
                {
                    CloseWriterInternal();

                    // Crear carpeta si no existe.
                    Directory.CreateDirectory(LogFolder);

                    var path = Path.Combine(LogFolder, $"rms-log-{today}.log");

                    // Append: si la app se reinicia el mismo dia, sigue
                    // escribiendo al mismo archivo sin borrar lo anterior.
                    _fileWriter = new StreamWriter(path, append: true) { AutoFlush = false };

                    _currentLogDate = today;
                    CurrentLogFilePath = path;
                }

                // Formato de linea: [YYYY-MM-DD HH:mm:ss.fff] [NIVEL] [CATEGORIA] Mensaje
                var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                           $"[{entry.LevelText}] " +
                           $"[{entry.Category}] " +
                           $"{entry.Message}";

                _fileWriter.WriteLine(line);
            }
            catch (Exception ex)
            {
                // No propagar errores de almacenamiento al hilo de audio/UI.
                Trace.TraceError("Fallo al escribir el log en disco ({0}): {1}",
                    CurrentLogFilePath ?? LogFolder, ex);
            }
        }
    }

    private async Task ProcessFileQueueAsync()
    {
        try
        {
            await foreach (var first in _fileQueue.Reader.ReadAllAsync())
            {
                WriteToFile(first);
                while (_fileQueue.Reader.TryRead(out var next))
                    WriteToFile(next);

                lock (_fileGate)
                {
                    try { _fileWriter?.Flush(); }
                    catch (Exception ex) { Trace.TraceError("Fallo al vaciar el log: {0}", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("El worker del log finalizo con error: {0}", ex);
        }
    }

    /// -------------Cierra el StreamWriter actual (si lo hay).</summary>
    private void CloseWriterInternal()
    {
        try { _fileWriter?.Dispose(); } catch { }
        _fileWriter = null;
    }

    /// -------------
    /// Fuerza el volcado y cierre del archivo actual. Se llama desde el
    /// shutdown de la app para garantizar que no queden lineas sin escribir.
    /// -------------
    public void Dispose()
    {
        _fileQueue.Writer.TryComplete();
        try { _fileWriterTask.GetAwaiter().GetResult(); } catch { }

        lock (_fileGate)
        {
            CloseWriterInternal();
            CurrentLogFilePath = null;
            _currentLogDate = null;
        }
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private static string BuildLogFolderPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "log");
    }
}
