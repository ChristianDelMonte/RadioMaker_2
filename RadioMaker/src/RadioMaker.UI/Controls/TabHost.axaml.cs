using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RadioMaker.UI.ViewModels;
using System.IO;

namespace RadioMaker.UI.Controls;

/// -------------
/// Code-behind del TabHost.
///
/// Ademas de la interaccion con pestañas (seleccionar, renombrar), soporta:
///   - Drop de archivos (audio = agregar a la pestaña, proyecto = nueva pestaña).
///   - Scroll horizontal por botones cuando hay overflow.
///   - Auto-scroll a la pestaña seleccionada si quedo fuera de vista.
/// -------------
public partial class TabHost : UserControl
{
    /// -------------Cantidad de pixeles que se desplaza por cada click en los botones.</summary>
    private const double ScrollStepPx = 240.0;

    public TabHost()
    {
        InitializeComponent();

        var itemsControl = this.FindControl<ItemsControl>("TabsItemsControl");
        if (itemsControl is not null)
        {
            itemsControl.AddHandler(
                PointerPressedEvent,
                OnTabsPointerPressed,
                RoutingStrategies.Tunnel);
        }

        // Scroll horizontal: suscribirse al ScrollChanged para habilitar
        // / deshabilitar los botones segun posicion.
        var scrollViewer = this.FindControl<ScrollViewer>("TabsScrollViewer");
        if (scrollViewer is not null)
        {
            scrollViewer.ScrollChanged += OnTabsScrollChanged;
        }

        // Drag & drop sobre las pestañas.
        AddHandler(DragDrop.DragOverEvent, OnTabDragOver);
        AddHandler(DragDrop.DropEvent, OnTabDrop);
        DragDrop.SetAllowDrop(this, true);

        // Suscribirse al cambio de pestaña seleccionada para auto-scroll.
        DataContextChanged += OnDataContextChanged;
    }

    // ---------------------------------------------------------------- Scroll

    private void OnScrollLeftClick(object? sender, RoutedEventArgs e)
    {
        var sv = this.FindControl<ScrollViewer>("TabsScrollViewer");
        if (sv is null) return;

        double newX = Math.Max(0, sv.Offset.X - ScrollStepPx);
        sv.Offset = new Vector(newX, sv.Offset.Y);
    }

    private void OnScrollRightClick(object? sender, RoutedEventArgs e)
    {
        var sv = this.FindControl<ScrollViewer>("TabsScrollViewer");
        if (sv is null) return;

        double maxOffset = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        double newX = Math.Min(maxOffset, sv.Offset.X + ScrollStepPx);
        sv.Offset = new Vector(newX, sv.Offset.Y);
    }

    private void OnTabsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtonsVisibility();
    }

    /// -------------
    /// Muestra/oculta los botones de scroll segun haya overflow o no.
    /// Habilita/deshabilita cada uno segun se pueda avanzar en esa direccion.
    /// -------------
    private void UpdateScrollButtonsVisibility()
    {
        var sv = this.FindControl<ScrollViewer>("TabsScrollViewer");
        var leftBtn = this.FindControl<Button>("ScrollLeftButton");
        var rightBtn = this.FindControl<Button>("ScrollRightButton");

        if (sv is null || leftBtn is null || rightBtn is null) return;

        bool hasOverflow = sv.Extent.Width > sv.Viewport.Width + 0.5;

        leftBtn.IsVisible = hasOverflow;
        rightBtn.IsVisible = hasOverflow;

        if (!hasOverflow) return;

        // Habilitar < solo si podemos ir a la izquierda.
        leftBtn.IsEnabled = sv.Offset.X > 0.5;

        // Habilitar > solo si podemos ir a la derecha.
        double maxOffset = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        rightBtn.IsEnabled = sv.Offset.X < maxOffset - 0.5;
    }

    // ---------------------------------------------------------------- Auto-scroll a la pestaña activa

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TabHostViewModel host)
        {
            host.SelectionChanged -= OnHostSelectionChanged;
            host.SelectionChanged += OnHostSelectionChanged;
        }
    }

    private void OnHostSelectionChanged(object? sender, TabItemViewModel? selectedTab)
    {
        if (selectedTab is null) return;

        // Esperar un tick al layout para que el contenedor visual exista.
        Dispatcher.UIThread.Post(() => ScrollToTab(selectedTab), DispatcherPriority.Background);
    }

    /// -------------
    /// Hace scroll para que la pestaña indicada quede visible dentro del
    /// ScrollViewer.
    /// -------------
    private void ScrollToTab(TabItemViewModel tab)
    {
        var sv = this.FindControl<ScrollViewer>("TabsScrollViewer");
        var itemsControl = this.FindControl<ItemsControl>("TabsItemsControl");
        if (sv is null || itemsControl is null) return;

        // Buscar el contenedor visual de la tab.
        Control? targetControl = null;
        foreach (var c in itemsControl.GetVisualDescendants().OfType<Control>())
        {
            if (ReferenceEquals(c.DataContext, tab))
            {
                targetControl = c;
                break;
            }
        }

        if (targetControl is null) return;

        // Posicion del control respecto al viewport del ScrollViewer.
        var transform = targetControl.TransformToVisual(sv);
        if (transform is null) return;

        var boundsInViewer = new Rect(targetControl.Bounds.Size)
            .TransformToAABB(transform.Value);

        double currentOffset = sv.Offset.X;
        double newOffset = currentOffset;

        // Si esta a la izquierda del viewport, scrollear a la izquierda.
        if (boundsInViewer.X < 0)
        {
            newOffset = currentOffset + boundsInViewer.X;
        }
        // Si esta a la derecha del viewport, scrollear a la derecha.
        else if (boundsInViewer.Right > sv.Viewport.Width)
        {
            newOffset = currentOffset + (boundsInViewer.Right - sv.Viewport.Width);
        }

        // Clamp.
        double maxOffset = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        newOffset = Math.Clamp(newOffset, 0, maxOffset);

        if (Math.Abs(newOffset - currentOffset) > 0.5)
        {
            sv.Offset = new Vector(newOffset, sv.Offset.Y);
        }
    }

    // ---------------------------------------------------------------- Clics

    private void OnTabsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TabHostViewModel host) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var tab = FindTabFromSource(e.Source as Visual);
        if (tab is null) return;

        if (e.ClickCount >= 2)
        {
            tab.BeginEdit();
            FocusEditingTextBox(tab);
        }
        else
        {
            host.SelectedTab = tab;
        }
    }

    private TabItemViewModel? FindTabFromSource(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Control c && c.DataContext is TabItemViewModel tab)
                return tab;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ---------------------------------------------------------------- Drag & drop

    private void OnTabDragOver(object? sender, DragEventArgs e)
    {
        if (HasValidData(e.DataTransfer))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;

        e.Handled = true;
    }

    private void OnTabDrop(object? sender, DragEventArgs e)
    {
        var path = ExtractFirstFilePath(e.DataTransfer);
        if (string.IsNullOrEmpty(path)) return;

        if (DataContext is not TabHostViewModel host) return;

        // ── Caso 1: proyecto nativo -> abrir en pestaña NUEVA.
        if (IsNativeProjectPath(path))
        {
            host.OpenNativeProjectInNewTab(path);
            e.Handled = true;
            return;
        }

        // ── Caso 2: audio -> agregar a la pestaña destino.
        if (!IsAudioPath(path))
        {
            e.Handled = true;
            return;
        }

        var tab = FindTabFromSource(e.Source as Visual);

        if (tab is not null)
            host.SelectedTab = tab;

        var targetTab = tab ?? host.SelectedTab;
        if (targetTab is null) return;

        host.AddFileToTab(targetTab, path);

        e.Handled = true;
    }

    private static bool HasValidData(IDataTransfer? data)
    {
        if (data is null) return false;

        try
        {
            var files = data.TryGetFiles();
            if (files is not null && files.Any()) return true;

            var text = data.TryGetText();
            if (!string.IsNullOrEmpty(text) && File.Exists(text)) return true;
        }
        catch { }

        return false;
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

    private static bool IsNativeProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        return ext.Equals(".rmf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".rme", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tnd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".rms", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".rcb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        if (ext.Equals(".rmf", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".tnd", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".rms", StringComparison.OrdinalIgnoreCase)) return false;
        if (ext.Equals(".rcb", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    // ---------------------------------------------------------------- Edicion inline

    private void FocusEditingTextBox(TabItemViewModel tab)
    {
        var itemsControl = this.FindControl<ItemsControl>("TabsItemsControl");
        if (itemsControl is null) return;

        foreach (var container in itemsControl.GetVisualDescendants().OfType<Control>())
        {
            if (container.DataContext != tab) continue;
            if (container.Name != "TitleEdit") continue;
            if (container is not TextBox tb) continue;

            tb.KeyDown -= OnEditKeyDown;
            tb.KeyDown += OnEditKeyDown;
            tb.LostFocus -= OnEditLostFocus;
            tb.LostFocus += OnEditLostFocus;

            Dispatcher.UIThread.Post(() =>
            {
                tb.Focus();
                tb.SelectAll();
            });
            return;
        }
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not TabItemViewModel tab) return;

        if (e.Key == Key.Enter)
        {
            tab.CommitEdit();
            DetachEditEvents(tb);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tab.CancelEdit();
            DetachEditEvents(tb);
            e.Handled = true;
        }
    }

    private void OnEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not TabItemViewModel tab) return;

        tab.CommitEdit();
        DetachEditEvents(tb);
    }

    private void DetachEditEvents(TextBox tb)
    {
        tb.KeyDown -= OnEditKeyDown;
        tb.LostFocus -= OnEditLostFocus;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Menu "+": click sobre una carpeta favorita

    private void OnFavoriteFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.Tag is not RadioMaker.UI.Services.FavoriteFolderEntry entry) return;
        if (DataContext is not TabHostViewModel host) return;

        if (!entry.ExistsNow)
        {
            host.StatusMessage = $"La carpeta ya no existe: {entry.Path}";
            return;
        }

        host.AddFavoriteFolderTab(entry);
    }
}
