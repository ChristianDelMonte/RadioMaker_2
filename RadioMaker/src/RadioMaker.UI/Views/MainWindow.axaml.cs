using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI.Views;

/// -------------
/// Code-behind de la ventana principal.
///
/// Contiene TODOS los handlers referenciados por MainWindow.axaml:
///   - Modulo 1: barra de menu (Archivo / Edicion / Ver / Reproduccion / Ayuda).
///   - Modulo 2: barra de herramientas (transporte, selector de bus, ducking,
///     accesos rapidos a configuracion).
///   - Modulos de dialogo modales (config de audio, buses, VU meter, plugins).
///
/// Los comandos de reproduccion (Play/Pause/Stop) llegan desde la toolbar
/// via binding directo al MainWindowViewModel, no pasan por aca.
/// El toggle de Ducking tambien esta bindeado directo al VM.
/// -------------
public partial class MainWindow : Window
{
    // Indices de las pestañas del TabControl "CenterTabs" del modulo 3.
    private const int TabIndexSpectrum = 0;
    private const int TabIndexEqualizer = 1;
    private const int TabIndexLog = 2;

    // CONSTRUCTOR
    public MainWindow()
    {
        InitializeComponent();

        // En Linux, el metodo de extension no funciona de forma fiable.
        // Se usa la barra nativa del sistema. La barra personalizada se oculta.
        if (OperatingSystem.IsLinux())
        {
            WindowDecorations  = WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = false;
        }

        // Conectar los botones de la barra personalizada (solo Windows/macOS).
        var minimize = this.FindControl<Button>("MinimizeButton");
        if (minimize is not null)
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var maximize = this.FindControl<Button>("MaximizeButton");
        if (maximize is not null)
            maximize.Click += (_, _) => ToggleMaximize();

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
            close.Click += (_, _) => Close();

        // Suscribirse a cambios de estado para actualizar el icono de maximizar.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.IsMaximized = WindowState == WindowState.Maximized;
            }
        };
        // Suscribirse a la solicitud de abrir la ventana de configuracion
        // en una seccion concreta (la dispara el panel de clima).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenSettingsRequested -= OnOpenSettingsRequested;
                vm.OpenSettingsRequested += OnOpenSettingsRequested;

                vm.OpenEmergencySettingsRequested -= OnOpenEmergencySettingsRequested;
                vm.OpenEmergencySettingsRequested += OnOpenEmergencySettingsRequested;

                vm.ConfirmStopEmergencyRequested -= OnConfirmStopEmergencyRequested;
                vm.ConfirmStopEmergencyRequested += OnConfirmStopEmergencyRequested;
            }
        };
        // Doble clic sobre la barra de titulo -> maximizar/restaurar.
        var titleBar = this.FindControl<Border>("CustomTitleBar");
        if (titleBar is not null)
        {
            titleBar.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

                // No arrastrar si el click fue sobre un boton.
                var source = e.Source as Control;
                if (source is not null)
                {
                    var parent = source.Parent;
                    while (parent is not null)
                    {
                        if (parent is Button) return;
                        parent = (parent as Control)?.Parent;
                    }
                }

                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                    e.Handled = true;
                }
            };
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Ciclo de vida
    // ═════════════════════════════════════════════════════════════════════

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            try { vm.SaveWorkspace(); } catch { }
        }

        // Flush del log a disco por si la app se cierra sin pasar por
        // ShutdownRequested (Alt+F4, cierre del WM, etc.).
        try { RadioMaker.Core.Logging.AppLogger.Service.Dispose(); } catch { }

        base.OnClosing(e);
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Archivo
    // ═════════════════════════════════════════════════════════════════════

    private async void OnMenuOpenFile(object? sender, RoutedEventArgs e)
    {
        await OpenAudioFileAsync();
    }

    private async void OnMenuSaveActiveTab(object? sender, RoutedEventArgs e)
    {
        await SaveActiveTabInternalAsync(forceDialog: false);
    }

    private async void OnMenuSaveActiveTabAs(object? sender, RoutedEventArgs e)
    {
        await SaveActiveTabInternalAsync(forceDialog: true);
    }

    private async void OnMenuGeneratePlaylist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Buscar una pestaña de Playlist ACTIVA (selected) en el host
        // del centro. Si no hay, creamos una nueva.
        PlaylistEditorViewModel? target =
            vm.TabHostCenter.SelectedTab?.Content as PlaylistEditorViewModel;

        if (target is null)
        {
            // Crear pestaña nueva en el host del centro.
            vm.TabHostCenter.AddPlaylistTabCommand.Execute(null);

            target = vm.TabHostCenter.SelectedTab?.Content as PlaylistEditorViewModel;

            if (target is null)
            {
                vm.StatusMessage = "No se pudo abrir una pestaña de playlist.";
                return;
            }
        }

        // Snapshot del estado para el warning.
        bool hasContent = target.TrackCount > 0;

        var genVm = new PlaylistGeneratorWindowViewModel(
            hasExistingContent: hasContent,
            existingTrackCount: target.TrackCount,
            existingPlaylistName: target.PlaylistName);

        var win = new PlaylistGeneratorWindow { DataContext = genVm };

        var ok = await win.ShowDialog<bool>(this);
        if (!ok) return;
        if (!genVm.HasPreview) return;

        // Aplicar al VM target.
        var paths = genVm.PreviewTracks.Select(p => p.FilePath).ToList();
        target.ReplaceTracksFromGenerator(paths);

        vm.StatusMessage = $"Playlist generada: {paths.Count} pista(s).";
    }

    private void OnMenuExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Archivo > Nuevo
    // ═════════════════════════════════════════════════════════════════════

    private void OnMenuNewTanda(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TabHost.AddTandaTabCommand.Execute(null);
        vm.StatusMessage = "Nueva pestaña de Tanda creada.";
    }

    private void OnMenuNewScheduled(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TabHost.AddScheduledTabCommand.Execute(null);
        vm.StatusMessage = "Nueva pestaña de Programados creada.";
    }

    private void OnMenuNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TabHost.AddPlaylistTabCommand.Execute(null);
        vm.StatusMessage = "Nueva pestaña de Playlist creada.";
    }

    private void OnMenuNewCart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Los Cart solo se pueden abrir en la zona central.
        vm.TabHostCenter.AddCartTabCommand.Execute(null);
        vm.StatusMessage = "Nueva pestaña de Cart creada.";
    }

    private void OnMenuNewNotes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TabHost.AddNotesTabCommand.Execute(null);
        vm.StatusMessage = "Nueva pestaña de Notas creada.";
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Archivo > Abrir
    // ═════════════════════════════════════════════════════════════════════

    private async void OnMenuOpenTanda(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await PickFileAsync(
            "Abrir tanda",
            "Tanda",
            new[] { "*" + TandaStore.FileExtension });

        if (string.IsNullOrEmpty(path)) return;

        vm.TabHost.AddTandaTabCommand.Execute(null);

        if (vm.TabHost.SelectedTab?.Content is TandaEditorViewModel tanda)
        {
            tanda.LoadFromFile(path);
            vm.StatusMessage = $"Tanda abierta: {Path.GetFileName(path)}";
        }
    }

    private async void OnMenuOpenScheduled(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await PickFileAsync(
            "Abrir lista de programados",
            "Lista de programados",
            new[] { "*" + ScheduledListStore.FileExtension });

        if (string.IsNullOrEmpty(path)) return;

        vm.TabHost.AddScheduledTabCommand.Execute(null);

        if (vm.TabHost.SelectedTab?.Content is ScheduledListViewModel list)
        {
            list.LoadFromFile(path);
            vm.StatusMessage = $"Programados abiertos: {Path.GetFileName(path)}";
        }
    }

    private async void OnMenuOpenPlaylist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await PickFileAsync(
            "Abrir playlist",
            "Playlist .rmf",
            new[] { "*" + PlaylistStore.FileExtension });

        if (string.IsNullOrEmpty(path)) return;

        vm.TabHost.AddPlaylistTabCommand.Execute(null);

        if (vm.TabHost.SelectedTab?.Content is PlaylistEditorViewModel playlist)
        {
            playlist.LoadFromFile(path);
            vm.StatusMessage = $"Playlist abierta: {Path.GetFileName(path)}";
        }
    }

    private async void OnMenuOpenCart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await PickFileAsync(
            "Abrir botonera",
            "Botonera",
            new[] { "*" + CartBoardStore.FileExtension });

        if (string.IsNullOrEmpty(path)) return;

        // Los Cart solo se pueden abrir en la zona central.
        vm.TabHostCenter.AddCartTabCommand.Execute(null);

        if (vm.TabHostCenter.SelectedTab?.Content is CartBoardViewModel cart)
        {
            cart.LoadFromFile(path);
            vm.StatusMessage = $"Botonera abierta: {Path.GetFileName(path)}";
        }
    }

    private async void OnMenuOpenNotes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await PickFileAsync(
            "Abrir nota",
            "Texto plano",
            new[] { "*.txt" });

        if (string.IsNullOrEmpty(path)) return;

        vm.TabHost.AddNotesTabCommand.Execute(null);

        if (vm.TabHost.SelectedTab?.Content is NotesEditorViewModel notes)
        {
            notes.OpenFile(path);
            vm.StatusMessage = $"Nota abierta: {Path.GetFileName(path)}";
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Archivo > Guardar (segun pestaña activa)
    // ═════════════════════════════════════════════════════════════════════

    private async void OnMenuSaveTanda(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not TandaEditorViewModel tanda) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        await SaveTandaAsync(tanda, topLevel, vm, forceDialog: false);
    }

    private async void OnMenuSaveScheduled(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not ScheduledListViewModel list) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        await SaveScheduledListAsync(list, topLevel, vm, forceDialog: false);
    }

    private async void OnMenuSavePlaylist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not PlaylistEditorViewModel playlist) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        await SavePlaylistAsync(playlist, topLevel, vm, forceDialog: false);
    }

    private async void OnMenuSaveCart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not CartBoardViewModel cart) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        await SaveCartAsync(cart, topLevel, vm, forceDialog: false);
    }

    private async void OnMenuSaveNotes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not NotesEditorViewModel notes) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        await SaveNotesAsync(notes, topLevel, vm, forceDialog: false);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Abre el dialogo nativo de seleccion de archivo y devuelve la ruta
    /// elegida, o null si el usuario cancelo.
    /// -------------
    private async Task<string?> PickFileAsync(string title, string typeName, string[] patterns)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var filter = new FilePickerFileType(typeName)
        {
            Patterns = patterns
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { filter, FilePickerFileTypes.All }
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    /// -------------
    /// Guarda un CartBoard. Si no tiene archivo asignado, abre el dialogo
    /// de "Guardar como...".
    /// -------------
    private static async Task SaveCartAsync(
        CartBoardViewModel cart,
        TopLevel topLevel,
        MainWindowViewModel vm,
        bool forceDialog)
    {
        const string ext = CartBoardStore.FileExtension; // ".rcb"

        if (!forceDialog && !string.IsNullOrEmpty(cart.LoadedFilePath))
        {
            cart.SaveToFile(cart.LoadedFilePath);
            vm.StatusMessage = $"Botonera guardada: {Path.GetFileName(cart.LoadedFilePath)}";
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar botonera",
            SuggestedFileName = SanitizeFileName(cart.BoardName) + ext,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Botonera")
                {
                    Patterns = new[] { "*" + ext }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        cart.SaveToFile(path);
        vm.StatusMessage = $"Botonera guardada: {Path.GetFileName(path)}";
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Edicion
    // ═════════════════════════════════════════════════════════════════════

    private async void OnMenuEditBlock(object? sender, RoutedEventArgs e)
    {
        await EditSelectedScheduledItemAsync(ScheduledItemKind.Block, "Bloque");
    }

    private async void OnMenuEditTandaItem(object? sender, RoutedEventArgs e)
    {
        await EditSelectedScheduledItemAsync(ScheduledItemKind.Tanda, "Tanda");
    }

    private async void OnMenuEditPlaylistItem(object? sender, RoutedEventArgs e)
    {
        await EditSelectedScheduledItemAsync(ScheduledItemKind.Playlist, "Playlist");
    }

    /// -------------
    /// Abre el editor del item seleccionado en la pestaña de Programados
    /// activa, validando que sea del tipo esperado.
    /// -------------
    private async Task EditSelectedScheduledItemAsync(ScheduledItemKind expectedKind, string label)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.ActiveTab?.Content is not ScheduledListViewModel list) return;
        if (list.SelectedItem is null) return;
        if (list.SelectedItem.Kind != expectedKind) return;

        var editorVm = new ScheduledItemEditorViewModel(list.SelectedItem);
        var win = new ScheduledItemEditorWindow(vm.SupportedExtensions) { DataContext = editorVm };

        var ok = await win.ShowDialog<bool>(this);

        if (ok)
        {
            vm.StatusMessage = $"{label} actualizado: {list.SelectedItem.DisplayTitle}";
        }
    }

    private async void OnMenuEditTags(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var filePath = vm.GetSelectedAudioFilePath();
        if (string.IsNullOrEmpty(filePath)) return;
        if (!File.Exists(filePath)) return;

        try
        {
            // Cargar los tags y abrir el editor.
            var fileVm = new AudioFileViewModel(filePath);
            await fileVm.LoadTagsAsync();

            var editorVm = new AudioTagEditorViewModel(fileVm);
            var win = new AudioTagEditorWindow { DataContext = editorVm };

            var ok = await win.ShowDialog<bool>(this);

            if (ok)
                vm.StatusMessage = $"Tags guardados: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Error al editar tags: {ex.Message}";
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Ver
    // ═════════════════════════════════════════════════════════════════════

    private void OnMenuShowSpectrum(object? sender, RoutedEventArgs e)
        => SelectCenterTab(TabIndexSpectrum);

    private void OnMenuShowEq(object? sender, RoutedEventArgs e)
        => SelectCenterTab(TabIndexEqualizer);

    private void OnMenuShowLog(object? sender, RoutedEventArgs e)
        => SelectCenterTab(TabIndexLog);

    private void OnMenuReloadLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        try { vm.PlaybackLog.ReloadSnapshotCommand.Execute(null); } catch { }
        vm.StatusMessage = "Log recargado.";
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Reproduccion (y toolbar Stop all)
    // ═════════════════════════════════════════════════════════════════════

    private void OnMenuPlay(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.PlayCommand.CanExecute(null))
            vm.PlayCommand.Execute(null);
    }

    private void OnMenuPause(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.PauseCommand.CanExecute(null))
            vm.PauseCommand.Execute(null);
    }

    private void OnMenuStop(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.StopCommand.CanExecute(null))
            vm.StopCommand.Execute(null);
    }

    private void OnMenuStopAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        try { vm.StopAllPlayback(); }
        catch (Exception ex) { vm.StatusMessage = $"Error al detener todo: {ex.Message}"; }
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Complementos
    //
    // Los complementos (RM Wave, RM Voice, RM Cast) son aplicaciones
    // externas que el usuario puede instalar junto a RadioMaker. Se
    // buscan en la carpeta "Complementos" al lado del ejecutable, o en
    // la misma carpeta, o en "complementos" (case-insensitive en Win).
    // ═════════════════════════════════════════════════════════════════════

    private void OnMenuLaunchRMWave(object? sender, RoutedEventArgs e)
        => LaunchComplement("RMWave", "RM Wave");

    private void OnMenuLaunchRMVoice(object? sender, RoutedEventArgs e)
        => LaunchComplement("RMVoice", "RM Voice");

    private void OnMenuLaunchRMCast(object? sender, RoutedEventArgs e)
        => LaunchComplement("RMCast", "RM Cast");

    /// -------------
    /// Busca y lanza un complemento externo. Busca el ejecutable en varias
    /// ubicaciones posibles (con subcarpeta "Complementos" en español o
    /// ingles, o en el mismo directorio que RadioMaker.exe).
    /// -------------
    private void LaunchComplement(string baseName, string displayName)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        string exeName = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;
        var baseDir = AppContext.BaseDirectory;

        // Ubicaciones candidatas, en orden de preferencia.
        // Cada addon vive en su propia carpeta: Addons/{baseName}/
        // (empaquetado por los targets BuildXxxAddon / CopyXxxAddon del .csproj de RadioMaker).
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Addons", baseName, exeName),
            Path.Combine(baseDir, "Complementos", exeName),
            Path.Combine(baseDir, "complementos", exeName),
            Path.Combine(baseDir, "Extensions", exeName),
            Path.Combine(baseDir, "extensions", exeName),
            Path.Combine(baseDir, exeName),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;

            try
            {
                // ===== CROSS-PLATFORM =====
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? baseDir
                };
                psi.ArgumentList.Add("--from-radiomaker");

                Process.Start(psi);

                vm.StatusMessage = $"Iniciando {displayName}...";

                // ===== LOGGING =====
                try
                {
                    RadioMaker.Core.Logging.AppLogger.Info(
                        "ADDON-LAUNCHER",
                        $"{displayName} iniciado desde {path}");
                }
                catch { }

                return;
            }
            catch (Exception ex)
            {
                // ===== LOGGING =====
                try
                {
                    RadioMaker.Core.Logging.AppLogger.Error(
                        "ADDON-LAUNCHER",
                        $"Error al iniciar {displayName} desde {path}: {ex.Message}",
                        ex);
                }
                catch { }

                vm.StatusMessage = $"Error al iniciar {displayName}: {ex.Message}";
                return;
            }
        }

        vm.StatusMessage =
            $"No se encontro {displayName} ({exeName}). " +
            "Verifica que este en Addons/RMVoice/ junto a RadioMaker.exe.";
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1: Menu - Ayuda
    // ═════════════════════════════════════════════════════════════════════

    private void OnMenuAbout(object? sender, RoutedEventArgs e)
    {
        ShowAboutDialog();
    }

    // ═════════════════════════════════════════════════════════════════════
    // MODULO 1 (Edicion) + MODULO 2: Configuracion unificada
    //
    // Desde aca se accede al panel de configuracion que reune TODAS las
    // secciones (Fade, Silencios, Normalizacion, Ducking, Buses, Ruteo,
    // VU Meter, Plugins de formato, Apariencia).
    // ═════════════════════════════════════════════════════════════════════

    private async void OnConfigureAudioClick(object? sender, RoutedEventArgs e)
    {
        await OpenUnifiedSettingsAsync("fade");
    }

    // SE REMUEVE POR MODIFICACIONES EN LA BARRA DE HERRAMIENTAS
    //private void OnReloadBusesClick(object? sender, RoutedEventArgs e)
    //{
    //    if (DataContext is MainWindowViewModel vm)
    //        vm.ReloadBuses();
    //}

    // ═════════════════════════════════════════════════════════════════════
    // Helpers: apertura de archivo de audio
    // ═════════════════════════════════════════════════════════════════════

    /// -------------
    /// Abre el dialogo nativo y carga el archivo elegido en el bus activo.
    /// Reemplaza al antiguo OnBrowseClick del AXAML previo.
    /// -------------
    private async Task OpenAudioFileAsync()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var filters = new List<FilePickerFileType>();

        foreach (var (name, exts) in vm.GetFormatGroups())
        {
            var patterns = exts.Select(x => "*" + x).ToArray();
            string label = $"{name} ({string.Join("; ", patterns)})";
            filters.Add(new FilePickerFileType(label) { Patterns = patterns });
        }

        filters.Add(FilePickerFileTypes.All);

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar archivo de audio",
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.LoadFile(path);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers: guardado de la pestaña activa
    // ═════════════════════════════════════════════════════════════════════

    private async Task SaveActiveTabInternalAsync(bool forceDialog)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var tab = vm.TabHost.SelectedTab;
        if (tab is null)
        {
            vm.StatusMessage = "No hay una pestaña activa para guardar.";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        switch (tab.Content)
        {
            case ScheduledListViewModel sl:
                await SaveScheduledListAsync(sl, topLevel, vm, forceDialog);
                break;

            case TandaEditorViewModel tn:
                await SaveTandaAsync(tn, topLevel, vm, forceDialog);
                break;

            case PlaylistEditorViewModel pl:
                await SavePlaylistAsync(pl, topLevel, vm, forceDialog);
                break;

            case NotesEditorViewModel notes:
                await SaveNotesAsync(notes, topLevel, vm, forceDialog);
                break;

            default:
                vm.StatusMessage = $"La pestaña '{tab.Title}' no admite guardado.";
                break;
        }
    }

    private static async Task SaveScheduledListAsync(
        ScheduledListViewModel sl,
        TopLevel topLevel,
        MainWindowViewModel vm,
        bool forceDialog)
    {
        const string ext = ScheduledListStore.FileExtension; // ".rms"

        if (!forceDialog && !string.IsNullOrEmpty(sl.LoadedFilePath))
        {
            sl.SaveToFile(sl.LoadedFilePath);
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar lista de programados",
            SuggestedFileName = SanitizeFileName(sl.ListName) + ext,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Lista de programados")
                {
                    Patterns = new[] { "*" + ext }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        sl.SaveToFile(path);
    }

    private static async Task SaveTandaAsync(
        TandaEditorViewModel tn,
        TopLevel topLevel,
        MainWindowViewModel vm,
        bool forceDialog)
    {
        const string ext = TandaStore.FileExtension; // ".tnd"

        if (!forceDialog && !string.IsNullOrEmpty(tn.LoadedFilePath))
        {
            tn.SaveToFile(tn.LoadedFilePath);
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar tanda",
            SuggestedFileName = SanitizeFileName(tn.TandaName) + ext,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Tanda")
                {
                    Patterns = new[] { "*" + ext }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        tn.SaveToFile(path);
    }

    private static async Task SavePlaylistAsync(
        PlaylistEditorViewModel pl,
        TopLevel topLevel,
        MainWindowViewModel vm,
        bool forceDialog)
    {
        const string ext = PlaylistStore.FileExtension; // ".rmf"

        if (!forceDialog && !string.IsNullOrEmpty(pl.LoadedFilePath))
        {
            pl.SaveToFile(pl.LoadedFilePath);
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar playlist",
            SuggestedFileName = SanitizeFileName(pl.PlaylistName) + ext,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Playlist .rmf")
                {
                    Patterns = new[] { "*" + ext }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        pl.SaveToFile(path);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers: seleccion de pestaña del modulo 3
    // ═════════════════════════════════════════════════════════════════════

    private void SelectCenterTab(int index)
    {
        try
        {
            var tabControl = this.FindControl<TabControl>("CenterTabs");
            if (tabControl is not null)
                tabControl.SelectedIndex = index;
        }
        catch { }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers: About (ventana con tabs, definida en AboutWindow.axaml)
    // ═════════════════════════════════════════════════════════════════════

    private void ShowAboutDialog()
    {
        var aboutVm = new AboutWindowViewModel();
        var window = new AboutWindow { DataContext = aboutVm };

        _ = window.ShowDialog(this);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Util
    // ═════════════════════════════════════════════════════════════════════
    private static async Task SaveNotesAsync(
    NotesEditorViewModel notes,
    TopLevel topLevel,
    MainWindowViewModel vm,
    bool forceDialog)
    {
        // Sin forzar dialogo y con archivo -> guardar directo.
        if (!forceDialog && notes.HasFilePath)
        {
            notes.Save();
            return;
        }

        var suggested = notes.HasFilePath
            ? Path.GetFileName(notes.LoadedFilePath)
            : "nota.txt";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar nota",
            SuggestedFileName = suggested,
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Texto plano")
                {
                    Patterns = new[] { "*.txt" }
                }
            }
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        notes.SaveTo(path);
    }

    private async void OnOpenSettingsRequested(object? sender, string sectionKey)
    {
        await OpenUnifiedSettingsAsync(sectionKey);
    }

    private async void OnOpenEmergencySettingsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Abrir la ventana de configuración directamente en la sección Emergencia.
        await OpenUnifiedSettingsAsync("emergency");

        // Al cerrar, el VM valida la config y arma si está OK.
        vm.OnEmergencySettingsClosed();
    }

    private async void OnConfirmStopEmergencyRequested(object? sender, Action<bool> callback)
    {
        var result = await ShowConfirmationDialogAsync(
            "Detener emergencia",
            "La emergencia está sonando al aire. ¿Deseas detenerla?\n\n" +
            "El watchdog quedará desactivado y el operador deberá reactivarlo manualmente.");

        callback(result);
    }

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message)
    {
        var result = false;

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };

        var cancelBtn = new Button { Content = "Cancelar", MinWidth = 100 };
        cancelBtn.Click += (_, _) => { result = false; dialog.Close(); };

        var confirmBtn = new Button
        {
            Content = "Detener",
            MinWidth = 100,
            Background = Avalonia.Media.Brushes.DarkRed,
            Foreground = Avalonia.Media.Brushes.White
        };
        confirmBtn.Click += (_, _) => { result = true; dialog.Close(); };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(confirmBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;

        await dialog.ShowDialog(this);
        return result;
    }

    /// -------------
    /// Alterna entre maximizado y restaurado.
    /// -------------
    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "sin_nombre";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Trim();
    }

    /// -------------
    /// Abre la ventana unificada de configuracion, opcionalmente posicionada
    /// en una seccion concreta (por ejemplo "buses", "vumeter" o "plugins").
    ///
    /// Al cerrar, recarga la lista de buses del MainWindowViewModel por si
    /// el usuario agrego/elimino/edito algun bus desde la seccion "Buses".
    /// -------------
    private async Task OpenUnifiedSettingsAsync(string initialSectionKey)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Propiedad publica: reemplaza al reflection sobre _engine.
        var engine = vm.AudioEngine;

        // Construir todos los sub-VMs que componen la ventana unificada.
        var busesVm = new AudioBusesWindowViewModel(engine);
        var vuMeterVm = new VuMeterSettingsWindowViewModel(vm.VuSettings);
        var pluginsVm = new PluginsWindowViewModel(engine);

        var settingsVm = new AudioSettingsWindowViewModel(
            vm.Fade,
            vm.Silence,
            vm.Normalization,
            vm.Ducking,
            busesVm,
            vuMeterVm,
            pluginsVm,
            vm.LocutionPanel,
            vm.Behavior,
            vm.Emergency,
            vm);

        // Posicionar en la seccion pedida ANTES de mostrar la ventana.
        if (!string.IsNullOrEmpty(initialSectionKey))
            settingsVm.SelectSection(initialSectionKey);

        var window = new AudioSettingsWindow { DataContext = settingsVm };
        await window.ShowDialog(this);

        // Persistir la configuracion del VU meter al cerrar (por si el
        // usuario cambio algo y no paso por el boton "Cerrar" original).
        try { RadioMaker.UI.Configuration.VuMeterSettingsStore.Save(vm.VuSettings); } catch { }

        // Refrescar la lista de buses del toolbar por si cambio algo.
        vm.ReloadBuses();
    }
}
