using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Services;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Views;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind de la botonera.
///
/// Interaccion:
///   - Clic simple = seleccionar.
///   - Doble clic   = reproducir/detener el boton.
///   - Clic derecho = menu contextual (Editar, Reproducir, Detener, Limpiar).
///   - Drag &amp; drop  = asignar/reemplazar archivo al boton.
///
/// IMPORTANTE sobre el drag &amp; drop:
///   En Avalonia los eventos de DragDrop (DragOver / Drop / DragLeave) solo
///   tienen fase BUBBLE, NO tienen fase Tunnel. Si se registran con
///   RoutingStrategies.Tunnel, el handler NUNCA se ejecuta.
///
///   Para que cada boton sea un drop target independiente (y el drag
///   funcione tanto en botones vacios como en botones con audio), cada
///   Border del DataTemplate tiene DragDrop.AllowDrop="True". Asi el
///   evento se dispara en el Border del boton (o en algun hijo), y bubblea
///   hasta este UserControl, donde lo procesamos.
///
///   Los handlers se registran con handledEventsToo: true por las dudas
///   de que algun hijo marque el evento como handled.
/// -------------
public partial class CartBoardView : UserControl
{
    /// -------------
    /// Ultimo boton sobre el que el drag estaba posicionado. Se usa para
    /// apagar el highlight del boton anterior cuando el cursor se mueve a
    /// otro boton o sale del control.
    /// -------------
    private CartButtonViewModel? _lastDragOverButton;

    public CartBoardView()
    {
        InitializeComponent();

        // Drag con BUBBLE (default de Avalonia). El hit-target es el Border
        // del boton (gracias al AllowDrop="True" en el DataTemplate), y el
        // evento bubblea hasta este UserControl.
        AddHandler(DragDrop.DragOverEvent, OnDragOver,
            RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop,
            RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // PointerPressed si va con Tunnel: se dispara antes que los hijos
        // y permite que el click sobre cualquier parte del boton (incluido
        // el TextBlock del titulo) active la seleccion.
        AddHandler(PointerPressedEvent, OnButtonPressed, RoutingStrategies.Tunnel);
    }

    private CartBoardViewModel? GetVm() => DataContext as CartBoardViewModel;

    private Window? GetOwnerWindow() => TopLevel.GetTopLevel(this) as Window;

    // ─────────────────────────────────────────────────────────────────────
    // Interaccion con botones

    private void OnButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        var buttonVm = FindButtonFromSource(e.Source as Visual);
        if (buttonVm is null) return;
        if (GetVm() is not { } vm) return;

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            vm.SelectedButton = buttonVm;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
        {
            vm.SelectedButton = buttonVm;
            vm.TriggerButton(buttonVm);
            e.Handled = true;
            return;
        }

        vm.SelectedButton = buttonVm;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Menu contextual

    private void OnContextEditClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender);
        if (buttonVm is null) return;

        _ = OpenEditorAsync(buttonVm);
    }

    private void OnContextEditInRMWaveClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender) ?? GetVm()?.SelectedButton;
        if (buttonVm is null) return;
        if (string.IsNullOrWhiteSpace(buttonVm.FilePath)) return;

        var boardVm = GetVm();
        if (!RMWaveLauncher.OpenFile(buttonVm.FilePath, out var error))
        {
            if (boardVm is not null)
                boardVm.StatusMessage = error ?? "No se pudo abrir en RMWave.";
        }
        else
        {
            if (boardVm is not null)
                boardVm.StatusMessage = $"Abriendo {System.IO.Path.GetFileName(buttonVm.FilePath)} en RMWave...";
        }
    }

    private async void OnContextEditTagsClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender);
        if (buttonVm is null) return;

        var filePath = buttonVm.FilePath;
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (!File.Exists(filePath))
        {
            var boardVm = GetVm();
            if (boardVm is not null)
                boardVm.StatusMessage = $"Archivo no encontrado: {Path.GetFileName(filePath)}";
            return;
        }

        try
        {
            // Cargar los tags y abrir el editor.
            var fileVm = new AudioFileViewModel(filePath);
            await fileVm.LoadTagsAsync();

            var editorVm = new AudioTagEditorViewModel(fileVm);
            var win = new AudioTagEditorWindow { DataContext = editorVm };

            var owner = GetOwnerWindow();
            if (owner is null) return;

            var ok = await win.ShowDialog<bool>(owner);

            if (ok)
            {
                var boardVm = GetVm();
                if (boardVm is not null)
                    boardVm.StatusMessage = $"Tags guardados: {Path.GetFileName(filePath)}";
            }
        }
        catch (Exception ex)
        {
            var boardVm = GetVm();
            if (boardVm is not null)
                boardVm.StatusMessage = $"Error al editar tags: {ex.Message}";
        }
    }

    private void OnContextPlayClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender);
        if (buttonVm is null) return;

        GetVm()?.TriggerButton(buttonVm);
    }

    private void OnContextStopClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender);
        if (buttonVm is null) return;

        GetVm()?.StopAllCommand.Execute(null);
    }

    private void OnContextClearClick(object? sender, RoutedEventArgs e)
    {
        var buttonVm = GetContextButtonVm(sender);
        if (buttonVm is null) return;

        buttonVm.Clear();
    }

    private CartButtonViewModel? GetContextButtonVm(object? sender)
    {
        if (sender is not MenuItem mi) return null;

        var cm = (mi.Parent as ContextMenu) ?? (mi.GetVisualParent() as ContextMenu);

        return GetVm()?.SelectedButton;
    }

    private async System.Threading.Tasks.Task OpenEditorAsync(CartButtonViewModel buttonVm)
    {
        var owner = GetOwnerWindow();
        if (owner is null) return;

        var editorVm = new CartButtonEditorViewModel(buttonVm);
        var supportedAudioExtensions = owner.DataContext is MainWindowViewModel mainVm
            ? mainVm.SupportedExtensions
            : null;
        var win = new CartButtonEditorWindow(supportedAudioExtensions) { DataContext = editorVm };

        var result = await win.ShowDialog<bool>(owner);

        if (result)
        {
            var vm = GetVm();
            if (vm is not null)
                vm.StatusMessage = $"Boton actualizado: {buttonVm.DisplayLabel}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Drag & drop (SOLO audios)

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Datos invalidos (no audio): no destacamos nada, cursor "no-drop".
        if (!HasAudioData(e.DataTransfer))
        {
            ClearDragOver();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Identificar el boton bajo el cursor (puede ser null si estamos
        // sobre un espacio vacio de la grilla o fuera de un boton).
        var buttonVm = FindButtonFromSource(e.Source as Visual);

        // Actualizar el highlight: apagar el anterior, encender el nuevo.
        UpdateDragOverHighlight(buttonVm);

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        // El drag salio del control: apagar cualquier highlight.
        ClearDragOver();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // Apagar el highlight ANTES de hacer cualquier otra cosa: el
        // usuario ya solto, el boton debe volver a su estado normal.
        ClearDragOver();

        var path = ExtractFirstFilePath(e.DataTransfer);
        if (string.IsNullOrEmpty(path))
        {
            e.Handled = true;
            return;
        }

        // Filtrar: solo aceptamos archivos de audio.
        if (!IsAudioPath(path))
        {
            var vmErr = GetVm();
            if (vmErr is not null)
                vmErr.StatusMessage = $"'{Path.GetFileName(path)}' no es un audio. La botonera solo acepta archivos de audio.";
            e.Handled = true;
            return;
        }

        var buttonVm = FindButtonFromSource(e.Source as Visual);
        if (buttonVm is null)
        {
            e.Handled = true;
            return;
        }

        // Asignar al boton. Si ya tenia un archivo, se REEMPLAZA sin
        // pedir confirmacion (es lo esperado en drag & drop).
        var boardVm = GetVm();
        boardVm?.AssignFileToButton(buttonVm, path);

        e.Handled = true;
    }

    /// -------------
    /// Actualiza el highlight de drag:
    ///   - Si el boton es el mismo que ya estaba destacado: no hace nada.
    ///   - Si es distinto: apaga el anterior y enciende el nuevo.
    ///   - Si es null (espacio vacio): apaga el anterior.
    /// -------------
    private void UpdateDragOverHighlight(CartButtonViewModel? buttonVm)
    {
        if (ReferenceEquals(buttonVm, _lastDragOverButton)) return;

        if (_lastDragOverButton is not null)
            _lastDragOverButton.IsDragOver = false;

        if (buttonVm is not null)
            buttonVm.IsDragOver = true;

        _lastDragOverButton = buttonVm;
    }

    /// -------------Apaga cualquier highlight de drag activo.</summary>
    private void ClearDragOver()
    {
        if (_lastDragOverButton is not null)
        {
            _lastDragOverButton.IsDragOver = false;
            _lastDragOverButton = null;
        }
    }

    /// -------------
    /// Cursor de drop: acepta si el data transfer contiene al menos un
    /// archivo de audio. Cualquier otra cosa (proyectos nativos, texto,
    /// nada) se rechaza.
    /// -------------
    private static bool HasAudioData(IDataTransfer? data)
    {
        var path = ExtractFirstFilePath(data);
        return IsAudioPath(path);
    }

    private static string? ExtractFirstFilePath(IDataTransfer? data)
    {
        if (data is null) return null;

        try
        {
            var files = data.TryGetFiles();
            if (files is not null)
            {
                var first = files.FirstOrDefault();
                if (first is not null)
                {
                    var p = first.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(p)) return p;
                }
            }

            var text = data.TryGetText();
            if (!string.IsNullOrEmpty(text) && File.Exists(text))
                return text;
        }
        catch { }

        return null;
    }

    /// -------------
    /// True si el path es un archivo de audio reproducible.
    /// Rechaza explicitamente los proyectos nativos de RadioMaker.
    /// -------------
    private static bool IsAudioPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        // Extensiones nativas de proyectos: NO son audio.
        if (ext.Equals(".rmf", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".tnd", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".rms", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".rcb", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private CartButtonViewModel? FindButtonFromSource(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Control c && c.DataContext is CartButtonViewModel btn)
                return btn;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Nuevo / Abrir / Guardar

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        GetVm()?.NewBoard();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filter = new FilePickerFileType("Botonera")
        {
            Patterns = new[] { "*" + CartBoardStore.FileExtension }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir botonera (.rcb)",
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.LoadFromFile(path);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        if (!string.IsNullOrEmpty(vm.LoadedFilePath))
        {
            vm.SaveToFile(vm.LoadedFilePath);
            return;
        }

        await SaveAsAsync(vm);
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;
        await SaveAsAsync(vm);
    }

    private async System.Threading.Tasks.Task SaveAsAsync(CartBoardViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var suggestedName = SanitizeFileName(vm.BoardName) + CartBoardStore.FileExtension;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar botonera (.rcb)",
            SuggestedFileName = suggestedName,
            DefaultExtension = CartBoardStore.FileExtension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Botonera")
                {
                    Patterns = new[] { "*" + CartBoardStore.FileExtension }
                }
            }
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.SaveToFile(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Util

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "botonera";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }
}
