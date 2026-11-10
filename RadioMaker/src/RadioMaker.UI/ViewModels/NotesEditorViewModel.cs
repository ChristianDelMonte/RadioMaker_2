using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.UI.Services;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del editor de notas (texto plano).
///
/// Persistencia:
///   - Draft SIEMPRE actualizado en %APPDATA%/RmsAudioPlayer/notes/{DraftId}.txt
///     con debounce de 800 ms para no escribir en cada tecla.
///   - El archivo .txt guardado es opcional. Mientras no se guarde, la nota
///     solo existe en el draft (que igual persiste entre sesiones).
///
/// Al restaurar el workspace:
///   - Si hay draft -> se carga el draft.
///   - Si no hay draft pero hay archivo -> se carga el archivo.
/// </summary>
public sealed partial class NotesEditorViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _autosaveTimer;
    private bool _suppressDirtyMark;
    private bool _disposed;

    /// <summary>Identificador estable del draft asociado a esta pestaña.</summary>
    public string DraftId { get; }

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Se dispara cuando cambia el titulo visible (archivo cargado/guardado).</summary>
    public event EventHandler? TitleChanged;

    public bool HasFilePath => !string.IsNullOrEmpty(LoadedFilePath);

    /// <summary>Nombre a mostrar en la pestaña.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(LoadedFilePath))
                return Path.GetFileNameWithoutExtension(LoadedFilePath);
            return "Sin titulo";
        }
    }

    // ───── Constructor ───────────────────────────────────────────────────

    public NotesEditorViewModel() : this(Guid.NewGuid().ToString("N"))
    {
    }

    public NotesEditorViewModel(string draftId, string? initialFilePath = null)
    {
        DraftId = draftId ?? throw new ArgumentNullException(nameof(draftId));

        // 1. Intentar cargar el draft (tiene prioridad: representa la sesion
        //    mas reciente con todos los cambios).
        var draft = NotesDraftStore.LoadDraft(DraftId);

        if (draft is not null)
        {
            _suppressDirtyMark = true;
            try { Text = draft; }
            finally { _suppressDirtyMark = false; }

            _loadedFilePath = initialFilePath;
            _isDirty = false;
        }
        else if (!string.IsNullOrEmpty(initialFilePath) && File.Exists(initialFilePath))
        {
            // 2. Sin draft, pero hay archivo previo -> cargar el archivo.
            try
            {
                _suppressDirtyMark = true;
                try { Text = File.ReadAllText(initialFilePath); }
                finally { _suppressDirtyMark = false; }

                _loadedFilePath = initialFilePath;
                _isDirty = false;
            }
            catch
            {
                _loadedFilePath = null;
            }
        }

        // Autosave debounced.
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _autosaveTimer.Tick += (_, _) => FlushAutosave();
    }

    // ───── Autosave ──────────────────────────────────────────────────────

    partial void OnTextChanged(string value)
    {
        if (_suppressDirtyMark) return;

        IsDirty = true;
        StatusMessage = "Sin guardar";

        // Reiniciar el timer (debounce).
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void FlushAutosave()
    {
        _autosaveTimer.Stop();
        NotesDraftStore.SaveDraft(DraftId, Text);
    }

    // ───── Operaciones de archivo ────────────────────────────────────────

    /// <summary>Carga un archivo .txt en este editor. Devuelve true si funciono.</summary>
    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (!File.Exists(filePath)) return false;

        try
        {
            var content = File.ReadAllText(filePath);

            _suppressDirtyMark = true;
            try { Text = content; }
            finally { _suppressDirtyMark = false; }

            LoadedFilePath = filePath;
            IsDirty = false;
            StatusMessage = $"Abierto: {Path.GetFileName(filePath)}";

            NotesDraftStore.SaveDraft(DraftId, Text);

            OnPropertyChanged(nameof(HasFilePath));
            OnPropertyChanged(nameof(DisplayName));
            TitleChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al abrir: {ex.Message}";
            return false;
        }
    }

    /// <summary>Guarda en el archivo actual. Si no hay archivo, no hace nada.</summary>
    public bool Save()
    {
        if (string.IsNullOrEmpty(LoadedFilePath)) return false;
        return SaveTo(LoadedFilePath);
    }

    /// <summary>Guarda el contenido en la ruta indicada.</summary>
    public bool SaveTo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, Text ?? string.Empty);

            LoadedFilePath = filePath;
            IsDirty = false;
            StatusMessage = $"Guardado: {Path.GetFileName(filePath)}";

            // Mantener el draft actualizado (por si se cierra con crash).
            NotesDraftStore.SaveDraft(DraftId, Text);

            OnPropertyChanged(nameof(HasFilePath));
            OnPropertyChanged(nameof(DisplayName));
            TitleChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
            return false;
        }
    }

    /// <summary>Limpia el editor para empezar una nota nueva.</summary>
    public void NewDocument()
    {
        _suppressDirtyMark = true;
        try { Text = string.Empty; }
        finally { _suppressDirtyMark = false; }

        LoadedFilePath = null;
        IsDirty = false;
        StatusMessage = "Documento nuevo";

        NotesDraftStore.SaveDraft(DraftId, string.Empty);

        OnPropertyChanged(nameof(HasFilePath));
        OnPropertyChanged(nameof(DisplayName));
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _autosaveTimer.Stop(); } catch { }

        // Volcado final del contenido al draft.
        NotesDraftStore.SaveDraft(DraftId, Text);

        // Si el contenido esta guardado en un archivo y no hay cambios
        // pendientes, el draft ya no hace falta.
        if (!IsDirty && HasFilePath)
        {
            NotesDraftStore.DeleteDraft(DraftId);
        }
    }
}