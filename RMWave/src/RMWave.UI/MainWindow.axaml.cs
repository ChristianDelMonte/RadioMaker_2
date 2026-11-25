using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ManagedBass;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Material.Icons.Avalonia;
using RMWave.UI.Localization;
using Material.Icons;
using RMWave.UI.Controls;
using RMWave.UI.Themes;
using RMWave.Core.Audio;
using RMWave.Core.Models;


namespace RMWave.UI;

public partial class MainWindow : Controls.CustomChromeWindow
{
    private const double TimelineHeight = 60;
    private const double ClipHeight = 36;
    private const double RowHeight = 46;
    private const double TimeRulerHeight = 16; // NUEVO: espacio para la regla de tiempo

    private readonly TimelinePlayer _engine = new(ownsBass: true);
    private readonly WaveformCache _waveformCache = new();
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };

    private bool _updatingSlider;
    private bool _hasUnsavedChanges = false;
    private bool _loadingDevices = false;

    private readonly Dictionary<Guid, TextBlock> _trackHeaders = new();
    private readonly Dictionary<Guid, Canvas> _trackCanvases = new();
    private readonly Dictionary<Guid, Line> _trackCursors = new();
    private readonly Dictionary<Guid, ScrollViewer> _trackScrollViewers = new();

    // Controles de loop por pista (para actualizarlos desde el doble click).
    private readonly Dictionary<Guid, CheckBox> _trackLoopChecks = new();
    private readonly Dictionary<Guid, TextBox> _trackLoopStartBoxes = new();
    private readonly Dictionary<Guid, TextBox> _trackLoopEndBoxes = new();

    // Línea de referencia temporal (click simple).
    private readonly Dictionary<Guid, double> _tempMarkers = new();

    // Timer para distinguir click simple de doble click.
    private DispatcherTimer? _clickTimer;
    private Guid _pendingClickTrackId;
    private double _pendingClickTime;
    private ProjectTrack? _selectedTrack;
    private AudioClip? _selectedClip;

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartClipStartTime;
    private int _dragStartClipRow;
    private AudioClip? _draggedClip;
    private ProjectTrack? _draggedTrack;
    private Border? _draggedBorder;
    private string? _currentProjectPath = null;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindow()
    {
        InitializeComponent();
        InitializeCustomChrome();

        _timer.Tick += (sender, args) => TimerTick();

        Opened += (sender, args) => InitializeAudio();
        Opened += (sender, args) => _ = TryOpenStartupFileAsync();
        Closing += (sender, args) => ShutdownAudio();

        SelectedStart.KeyDown += SelectedEditor_KeyDown;
        SelectedOffset.KeyDown += SelectedEditor_KeyDown;
        SelectedDuration.KeyDown += SelectedEditor_KeyDown;
        SelectedGain.ValueChanged += OnSelectedGainChanged;

        PositionSlider.AddHandler(
            InputElement.PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(SliderSeekStart),
            RoutingStrategies.Tunnel);

        PositionSlider.AddHandler(
            InputElement.PointerReleasedEvent,
            new EventHandler<PointerReleasedEventArgs>(SliderSeekEnd),
            RoutingStrategies.Tunnel);

        ThemeManager.ThemeChanged += OnThemeChanged;
        ThemeManager.Load();
        BuildThemeMenu();

        // Timer para distinguir click simple de doble click en el timeline.
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _clickTimer.Tick += (s, e) =>
        {
            _clickTimer.Stop();
            ProcessSingleClick();
        };

        RecentProjectsManager.Load();
        RecentProjectsManager.ListChanged += BuildRecentMenu;
        BuildRecentMenu();
        UpdateWindowTitle();
    }

    // ==========================================
    // INICIALIZACIÓN Y CIERRE
    // ==========================================

    private void InitializeAudio()
    {
        try
        {
            if (_engine.Init())
            {
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.Ready");
                AddTrackButton.IsEnabled = true;
                UpdateTransportState();
                LoadAudioDevices();
            }
            else
            {
                StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.BassMixerError", Bass.LastError);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.BassLoadError", ex.Message);
        }
    }

    private void ShutdownAudio()
    {
        _timer.Stop();
        _engine.Dispose();
    }

    private async Task TryOpenStartupFileAsync()
    {
        // Ruta pasada por línea de comandos (--open-file o path directo).
        var path = Program.StartupFilePath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path))
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.FileMissing", path);
            return;
        }

        try
        {
            await OpenProjectByPath(path);
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.OpenedFromCommandLine", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.OpenFileError", System.IO.Path.GetFileName(path), ex.Message);
        }
    }

    // ==========================================
    // TRANSPORTE
    // ==========================================

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        // Si hay un loop activo y estamos al principio o al final,
        // comenzar directamente desde el inicio del loop.
        var (loopStart, loopEnd) = GetEffectiveLoopRange();
        bool hasLoop = loopStart >= 0 && loopEnd > loopStart;

        if (hasLoop)
        {
            var pos = _engine.GetPositionSeconds();
            bool atStart = pos < 0.05;
            bool atEnd = _engine.DurationSeconds > 0 && pos >= _engine.DurationSeconds - 0.05;

            if (atStart || atEnd)
            {
                _engine.Seek(loopStart);
            }
        }

        if (_engine.Play())
        {
            _timer.Start();
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.Playing");
        }
        else
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.PlayError", Bass.LastError);
        }
    }

    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        if (_engine.Pause())
        {
            _timer.Stop();
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.Paused");

            // Resetear el VU meter a cero.
            MasterVuMeter.SetLevels(MasterVuMeter.MinDb, MasterVuMeter.MinDb);
        }
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _engine.Stop();
        _timer.Stop();
        UpdatePositionUi();
        UpdatePlaybackCursor();
        StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.Stopped");

        // Resetear el VU meter a cero.
        MasterVuMeter.SetLevels(MasterVuMeter.MinDb, MasterVuMeter.MinDb);
    }

    private void TimerTick()
    {
        UpdatePositionUi();
        UpdatePlaybackCursor();

        // Verificar loop de reproducción.
        if (_engine.IsPlaying)
        {
            var (loopStart, loopEnd) = GetEffectiveLoopRange();

            if (loopStart >= 0 && loopEnd > loopStart + 0.05)
            {
                var currentPos = _engine.GetPositionSeconds();
                if (currentPos >= loopEnd)
                {
                    _engine.Seek(loopStart);
                    return;
                }
            }
        }

        // Verificar fin de reproduccion
        if (_engine.DurationSeconds > 0 &&
            _engine.GetPositionSeconds() >= _engine.DurationSeconds - 0.05)
        {
            _timer.Stop();
            _engine.Stop();
            UpdatePositionUi();
            UpdatePlaybackCursor();
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.PlaybackEnded");

            // Resetear el VU meter a cero.
            MasterVuMeter.SetLevels(MasterVuMeter.MinDb, MasterVuMeter.MinDb);
        }
        // Actualizar VU master.
        _engine.GetMasterLevelStereo(out float levelLeft, out float levelRight);
        MasterVuMeter.SetLevels(LinearToDb(levelLeft), LinearToDb(levelRight));
    }

    private void UpdatePositionUi()
    {
        var position = _engine.GetPositionSeconds();

        _updatingSlider = true;
        PositionSlider.Value = Math.Clamp(position, PositionSlider.Minimum, PositionSlider.Maximum);
        _updatingSlider = false;

        PositionText.Text = FormatTime(position);
    }

    private void PositionSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider)
            return;

        // Durante el arrastre, solo actualizar el texto (el seek se hace al soltar).
        PositionText.Text = FormatTime(PositionSlider.Value);
    }

    private void SliderSeekStart(object? sender, PointerPressedEventArgs e)
    {
        // Detener el timer para que no interfiera durante el arrastre.
        _timer.Stop();
    }

    private void SliderSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        // Hacer el seek a la posición donde soltó el usuario.
        _engine.Seek(PositionSlider.Value);
        PositionText.Text = FormatTime(PositionSlider.Value);
        UpdatePlaybackCursor();

        // Reiniciar el timer solo si estábamos reproduciendo.
        if (_engine.IsPlaying)
            _timer.Start();
    }

    // ==========================================
    // TÍTULO DE VENTANA
    // ==========================================
    private void UpdateWindowTitle()
    {
        var baseTitle = WaveLocalizationService.GetString("Loc.RMWave.Main.Title");

        var fileName = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? WaveLocalizationService.GetString("Loc.RMWave.Main.Untitled")
            : System.IO.Path.GetFileName(_currentProjectPath);
        Title = $"{baseTitle} - {fileName}";

        if (_hasUnsavedChanges)
            Title += " *";

        // La ventana usa una barra de título propia: mantenerla sincronizada
        // con el título nativo al abrir, guardar, crear y editar proyectos.
        var titleText = this.FindControl<TextBlock>("WindowTitleText");
        if (titleText is not null)
            titleText.Text = Title;
    }

    // ==========================================
    // TEMAS
    // ==========================================
    private void BuildThemeMenu()
    {
        ThemeMenu.Items.Clear();

        foreach (var theme in ThemeManager.Available)
        {
            var item = new MenuItem
            {
                Header = ThemeManager.GetDisplayName(theme),
                Tag = theme
            };

            if (theme == ThemeManager.Current)
            {
                item.Icon = new MaterialIcon
                {
                    Kind = MaterialIconKind.Check,
                    Width = 16,
                    Height = 16
                };
            }

            item.Click += ThemeMenuItem_Click;
            ThemeMenu.Items.Add(item);
        }
    }

    private void ThemeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: AppTheme theme })
        {
            ThemeManager.Apply(theme);
            BuildThemeMenu(); // refresca el check
        }
    }

    private void OnThemeChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Actualizar fondos de los canvas existentes.
            foreach (var canvas in _trackCanvases.Values)
            {
                canvas.Background = ThemeManager.Current.GetCanvasBrush();
            }

            RefreshTimeline();
        });
    }

    // ==========================================
    // MARCADORES DEL TIMELINE
    // ==========================================
    private async void AddMarker_Click(object? sender, RoutedEventArgs e)
    {
        // Si hay un click pendiente (línea de referencia aún no dibujada), procesarlo ya.
        if (_clickTimer != null && _clickTimer.IsEnabled)
        {
            _clickTimer.Stop();
            ProcessSingleClick();
        }

        double position;
        bool usedReferenceLine = false;

        // Si hay una línea de referencia (click en el timeline), usar esa posición.
        if (_tempMarkers.Count > 0)
        {
            position = _tempMarkers.Values.First();
            usedReferenceLine = true;
        }
        else
        {
            // Si no, usar la posición de reproducción.
            position = _engine.GetPositionSeconds();
        }

        var added = await AddMarkerAtPosition(position);

        // Si se agregó el marcador y se usó la línea de referencia, limpiarla.
        if (added && usedReferenceLine)
        {
            _tempMarkers.Clear();
            RefreshTimeline();
        }
    }

    private async Task<bool> AddMarkerAtPosition(double time)
    {
        var defaultName = $"Marcador {_engine.Project.Markers.Count + 1}";
        var name = await ShowMarkerNameDialog(defaultName);

        if (string.IsNullOrWhiteSpace(name)) return false;

        _engine.Project.Markers.Add(new TimelineMarker
        {
            Name = name,
            Time = time
        });

        RefreshTimeline();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.MarkerAdded", name, FormatTime(time));
        return true;
    }

    private async Task<string?> ShowMarkerNameDialog(string defaultName, string? title = null)
    {
        string? result = null;

        var dialog = new Window
        {
            Title = title ?? WaveLocalizationService.GetString("Loc.RMWave.Dialog.NewMarkerTitle"),
            Width = 380,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.AddMarkerName") });

        var nameBox = new TextBox { Text = defaultName };
        panel.Children.Add(nameBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var okButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Accept"), MinWidth = 90 };
        okButton.Click += (s, e) =>
        {
            result = nameBox.Text?.Trim();
            dialog.Close();
        };

        var cancelButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Cancel"), MinWidth = 90 };
        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(this);

        return result;
    }

    private async Task<double?> ShowMarkerTimeDialog(TimelineMarker marker)
    {
        double? result = null;

        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.EditMarkerTitle"),
            Width = 380,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.MarkerLabel", marker.Name),
            FontWeight = FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.MarkerPosition") });

        var timeBox = new TextBox { Text = FormatLoopTime(marker.Time) };
        panel.Children.Add(timeBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var okButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Accept"), MinWidth = 90 };
        okButton.Click += (s, e) =>
        {
            result = Math.Max(0, ParseLoopTime(timeBox.Text ?? ""));
            dialog.Close();
        };

        var cancelButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Cancel"), MinWidth = 90 };
        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(this);

        return result;
    }

    private async void MarkersList_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.MarkersTitle"),
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        void BuildContent()
        {
            var markers = _engine.Project.Markers.OrderBy(m => m.Time).ToList();

            var panel = new StackPanel
            {
                Margin = new Thickness(15),
                Spacing = 8
            };

            if (markers.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.NoMarkers"),
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }

            foreach (var marker in markers)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8
                };

                // Tiempo.
                row.Children.Add(new TextBlock
                {
                    Text = FormatTime(marker.Time),
                    Width = 80,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255))
                });

                // Nombre editable.
                var nameBox = new TextBox
                {
                    Text = marker.Name,
                    Width = 260,
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameBox.LostFocus += (s, ev) =>
                {
                    var newName = nameBox.Text?.Trim() ?? "";
                    if (newName != marker.Name)
                    {
                        marker.Name = newName;
                        RefreshTimeline();
                        MarkAsChanged();
                    }
                };
                row.Children.Add(nameBox);

                // Botón eliminar.
                var deleteBtn = new Button
                {
                    Content = new MaterialIcon
                    {
                        Kind = MaterialIconKind.DeleteOutline,
                        Width = 14,
                        Height = 14
                    },
                    Width = 30,
                    Height = 28,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(deleteBtn, WaveLocalizationService.GetString("Loc.RMWave.Track.DeleteMarker"));
                deleteBtn.Click += (s, ev) =>
                {
                    _engine.Project.Markers.Remove(marker);
                    RefreshTimeline();
                    MarkAsChanged();
                    BuildContent(); // Reconstruir la lista.
                };
                row.Children.Add(deleteBtn);

                panel.Children.Add(row);
            }

            // Botón cerrar.
            var closeButton = new Button
            {
                Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Close"),
                MinWidth = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0)
            };
            closeButton.Click += (s, ev) => dialog.Close();
            panel.Children.Add(closeButton);

            dialog.Content = new ScrollViewer { Content = panel };
        }

        BuildContent();

        await dialog.ShowDialog(this);
    }

    // ==========================================
    // ELIMINAR SELECCIÓN (RANGO ENTRE MARCADORES)
    // ==========================================

    /// <summary>Verifica si un tiempo está dentro de la selección (rango de loop efectivo).</summary>
    private bool IsInsideSelection(double time)
    {
        var (start, end) = GetEffectiveLoopRange();
        return start >= 0 && end > start && time >= start && time <= end;
    }

    /// <summary>Pide confirmación y elimina la selección.</summary>
    private async Task ConfirmAndDeleteSelection()
    {
        var (start, end) = GetEffectiveLoopRange();
        if (start < 0 || end <= start)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NoSelection");
            return;
        }

        var confirmed = await ShowMessageBox(
            WaveLocalizationService.GetString("Loc.RMWave.Dialog.DeleteSelectionTitle"),
            WaveLocalizationService.Format("Loc.RMWave.Dialog.DeleteSelectionMessage", FormatTime(start), FormatTime(end)));

        if (!confirmed) return;

        DeleteSelection(start, end);
    }

    /// <summary>
    /// Elimina el audio en el rango [start, end] de todas las pistas,
    /// desplaza el contenido posterior (ripple) y actualiza los marcadores.
    /// </summary>
    private void DeleteSelection(double start, double end)
    {
        double rangeLength = end - start;

        foreach (var track in _engine.Project.Tracks)
        {
            var toRemove = new List<AudioClip>();
            var toAdd = new List<AudioClip>();

            foreach (var clip in track.Clips)
            {
                double clipStart = clip.StartTime;
                double clipEnd = clip.EndTime;

                // Clip completamente antes del rango: sin cambios.
                if (clipEnd <= start)
                    continue;

                // Clip completamente después del rango: desplazar a la izquierda (ripple).
                if (clipStart >= end)
                {
                    clip.StartTime -= rangeLength;
                    continue;
                }

                // Clip totalmente dentro del rango: eliminarlo.
                if (clipStart >= start && clipEnd <= end)
                {
                    toRemove.Add(clip);
                    continue;
                }

                // El rango está totalmente dentro del clip: dividir en dos.
                if (clipStart < start && clipEnd > end)
                {
                    // Parte derecha (nuevo clip).
                    var rightClip = new AudioClip
                    {
                        FilePath = clip.FilePath,
                        Name = clip.Name,
                        StartTime = start,
                        SourceOffset = clip.SourceOffset + (end - clipStart),
                        Duration = clipEnd - end,
                        SourceFileDuration = clip.SourceFileDuration,
                        Gain = clip.Gain,
                        Row = clip.Row,
                        FadeIn = 0,
                        FadeOut = clip.FadeOut
                    };
                    toAdd.Add(rightClip);

                    // Parte izquierda (modificar el original).
                    clip.Duration = start - clipStart;
                    clip.FadeOut = 0;
                    continue;
                }

                // Clip empieza antes del rango y termina dentro: recortar la derecha.
                if (clipStart < start && clipEnd <= end)
                {
                    clip.Duration = start - clipStart;
                    clip.FadeOut = 0;
                    continue;
                }

                // Clip empieza dentro del rango y termina después: recortar la izquierda y desplazar.
                if (clipStart >= start && clipEnd > end)
                {
                    clip.SourceOffset += (end - clipStart);
                    clip.Duration = clipEnd - end;
                    clip.StartTime = start;
                    clip.FadeIn = 0;
                    continue;
                }
            }

            foreach (var c in toRemove) track.Clips.Remove(c);
            foreach (var c in toAdd) track.Clips.Add(c);

            // Limpiar clips con duración despreciable (seguridad por punto flotante).
            track.Clips.RemoveAll(c => c.Duration <= 0.01);

            // Desactivar el loop de la pista (la selección ya no existe).
            track.LoopEnabled = false;
        }

        // Actualizar marcadores: eliminar los que están en el rango, desplazar los posteriores.
        var markersInRange = _engine.Project.Markers
            .Where(m => m.Time >= start && m.Time <= end)
            .ToList();
        foreach (var m in markersInRange)
            _engine.Project.Markers.Remove(m);

        foreach (var m in _engine.Project.Markers)
        {
            if (m.Time > end)
                m.Time -= rangeLength;
        }

        // Limpiar selección de clip.
        ClearSelection();

        RefreshTimeline();
        UpdateTransportState();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.SelectionDeleted", FormatTime(start), FormatTime(end));
    }

    /// <summary>Menú contextual al hacer click derecho sobre la selección.</summary>
    private void ShowSelectionContextMenu(Control anchor)
    {
        var menu = new ContextMenu();

        var exportItem = new MenuItem
        {
            Header = WaveLocalizationService.GetString("Loc.RMWave.Main.ExportSelection"),
            Icon = new MaterialIcon { Kind = MaterialIconKind.ExportVariant, Width = 14, Height = 14 }
        };
        exportItem.Click += async (s, e) => await ExportSelectionAsync();
        menu.Items.Add(exportItem);

        var deleteItem = new MenuItem
        {
            Header = WaveLocalizationService.GetString("Loc.RMWave.Dialog.DeleteSelectionTitle"),
            Icon = new MaterialIcon { Kind = MaterialIconKind.DeleteOutline, Width = 14, Height = 14 }
        };
        deleteItem.Click += async (s, e) => await ConfirmAndDeleteSelection();
        menu.Items.Add(deleteItem);

        menu.Open(anchor);
    }

    // ==========================================
    // RANGO DE LOOP EFECTIVO
    // ==========================================

    /// <summary>
    /// Calcula el rango de loop efectivo considerando todas las pistas.
    /// Si varias pistas tienen loop activo, usa la intersección.
    /// Retorna (-1, -1) si no hay ningún loop activo.
    /// </summary>
    private (double Start, double End) GetEffectiveLoopRange()
    {
        double start = -1;
        double end = -1;

        foreach (var track in _engine.Project.Tracks)
        {
            if (track.LoopEnabled && track.LoopEnd > track.LoopStart + 0.05)
            {
                if (start < 0)
                {
                    start = track.LoopStart;
                    end = track.LoopEnd;
                }
                else
                {
                    // Intersección si hay varios loops activos.
                    start = Math.Max(start, track.LoopStart);
                    end = Math.Min(end, track.LoopEnd);
                }
            }
        }

        return (start, end);
    }

    // ==========================================
    // IMPORTAR MARCADORES DE REGIONES
    // ==========================================
    private async void ImportMarkers_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedClip == null || _selectedTrack == null)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.SelectClipForRegions");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.ImportRegionsTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Regions"))
                {
                    Patterns = new[] { "*.txt", "*.reg", "*.csv" }
                },
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.All"))
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0) return;

        var path = files[0].Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var regions = ParseRegionFile(path);
            if (regions.Count == 0)
            {
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NoValidRegions");
                return;
            }

            var clip = _selectedClip;
            int added = 0;

            foreach (var region in regions)
            {
                // Mapear el tiempo de la región (relativo al archivo) al timeline.
                double timelineTime = clip.StartTime + (region.Start - clip.SourceOffset);

                // Saltar regiones que queden fuera del rango del clip.
                if (timelineTime < clip.StartTime - 0.001 || timelineTime > clip.EndTime + 0.001)
                    continue;

                var name = string.IsNullOrWhiteSpace(region.Id)
                    ? $"Reg {added + 1:00}"
                    : region.Id;

                _engine.Project.Markers.Add(new TimelineMarker
                {
                    Name = name,
                    Time = timelineTime
                });
                added++;
            }

            RefreshTimeline();
            MarkAsChanged();

            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.MarkersImported", added, System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ImportMarkersError", ex.Message);
        }
    }

    /// <summary>
    /// Interpreta un archivo de tabla de regiones.
    /// Formato por línea: &lt;inicio&gt; &lt;fin&gt; &lt;id&gt;  (ej: 00:00:01.5 00:00:02.9 01)
    /// </summary>
    private List<(double Start, double End, string Id)> ParseRegionFile(string path)
    {
        var regions = new List<(double Start, double End, string Id)>();
        var lines = System.IO.File.ReadAllLines(path);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Saltar encabezados, pies y comentarios.
            if (line.StartsWith("start_region_table", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("end_region_table", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("#") || line.StartsWith("//")) continue;

            var parts = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (!TryParseRegionTime(parts[0], out double start)) continue;
            if (!TryParseRegionTime(parts[1], out double end)) continue;

            string id = parts.Length >= 3 ? parts[2] : "";

            regions.Add((start, end, id));
        }

        return regions;
    }

    /// <summary>
    /// Interpreta tiempos del tipo HH:MM:SS.d (ej: 00:00:01.5 = 1.5 segundos).
    /// </summary>
    private bool TryParseRegionTime(string text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(':');
        double result = 0;

        foreach (var part in parts)
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                result = result * 60 + val;
            else
                return false;
        }

        seconds = result;
        return true;
    }

    // ==========================================
    // MENÚ CONTEXTUAL DE MARCADORES
    // ==========================================

    private void ShowMarkerContextMenu(TimelineMarker marker, Control anchor)
    {
        var menu = new ContextMenu();

        // Renombrar
        var renameItem = new MenuItem
        {
            Header = WaveLocalizationService.GetString("Loc.RMWave.Marker.Rename"),
            Icon = new MaterialIcon { Kind = MaterialIconKind.Pencil, Width = 14, Height = 14 }
        };
        renameItem.Click += async (s, e) =>
        {
            var newName = await ShowMarkerNameDialog(marker.Name, WaveLocalizationService.GetString("Loc.RMWave.Dialog.RenameMarkerTitle"));
            if (!string.IsNullOrWhiteSpace(newName))
            {
                marker.Name = newName;
                RefreshTimeline();
                MarkAsChanged();
                StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.MarkerRenamed", newName);
            }
        };

        // Editar posición
        var editItem = new MenuItem
        {
            Header = WaveLocalizationService.GetString("Loc.RMWave.Marker.EditPosition"),
            Icon = new MaterialIcon { Kind = MaterialIconKind.ClockOutline, Width = 14, Height = 14 }
        };
        editItem.Click += async (s, e) =>
        {
            var newTime = await ShowMarkerTimeDialog(marker);
            if (newTime.HasValue)
            {
                marker.Time = newTime.Value;
                RefreshTimeline();
                MarkAsChanged();
                StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.MarkerMoved", marker.Name, FormatTime(newTime.Value));
            }
        };

        // Eliminar
        var deleteItem = new MenuItem
        {
            Header = WaveLocalizationService.GetString("Loc.RMWave.Marker.Delete"),
            Icon = new MaterialIcon { Kind = MaterialIconKind.DeleteOutline, Width = 14, Height = 14 },
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100))
        };
        deleteItem.Click += (s, e) =>
        {
            var name = marker.Name;
            _engine.Project.Markers.Remove(marker);
            RefreshTimeline();
            MarkAsChanged();
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.MarkerDeleted", name);
        };

        menu.Items.Add(renameItem);
        menu.Items.Add(editItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);

        menu.Open(anchor);
    }

    // ==========================================
    // GRABACION DE AUDIO
    // ==========================================

    private async void Record_Click(object? sender, RoutedEventArgs e)
    {
        var recordedFile = await ShowRecordDialog();

        if (string.IsNullOrEmpty(recordedFile))
            return;

        // Crear una pista nueva con el clip grabado.
        var track = _engine.AddTrack();
        track.Name = $"Grabación {DateTime.Now:HH:mm:ss}";

        var clip = _engine.AddClip(track.Id, recordedFile);
        if (clip != null)
        {
            clip.StartTime = 0;
            clip.Row = 0;

            AddTrackUi(track);
            UpdateTrackHeader(track);
            RefreshTimeline();
            UpdateTransportState();
            MarkAsChanged();

            // NUEVO: Calcular waveform para el archivo grabado.
            EnsureWaveformForClip(recordedFile);

            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.RecordingAdded", track.Name);
        }
        else
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.RecordingAddError");
        }
    }

    private async Task<string?> ShowRecordDialog()
    {
        string? resultPath = null;
        string? tempPath = null;

        var recordingEngine = new RecordingEngine();
        var levelTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        // ---- Construir la ventana ----
        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.RecordTitle"),
            Width = 480,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        // Dispositivo
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.RecordDevice") });

        var deviceCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var devices = RecordingEngine.GetRecordingDevices();
        foreach (var dev in devices)
        {
            deviceCombo.Items.Add(dev);
        }

        if (deviceCombo.Items.Count > 0)
            deviceCombo.SelectedIndex = 0;

        panel.Children.Add(deviceCombo);

        // Formato
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Format") });

        var formatCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        formatCombo.Items.Add("44100 Hz Estéreo");
        formatCombo.Items.Add("44100 Hz Mono");
        formatCombo.Items.Add("48000 Hz Estéreo");
        formatCombo.Items.Add("48000 Hz Mono");
        formatCombo.SelectedIndex = 0;

        panel.Children.Add(formatCombo);

        // VuMeter
        var vuMeter = new VuMeter
        {
            Orientation = VuOrientation.Horizontal,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ShowDbText = true,
            ShowPeakHold = true,
            DisplayMode = VuDisplayMode.Segmented
        };
        panel.Children.Add(vuMeter);

        // Duración
        var durationText = new TextBlock
        {
            Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.Duration", "00:00:00"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(durationText);

        // Botones
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var recordButton = new Button
        {
            Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Record"),
            MinWidth = 120,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68)),
            FontWeight = FontWeight.Bold
        };

        var stopButton = new Button
        {
            Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Stop"),
            MinWidth = 120,
            IsEnabled = false
        };

        var cancelButton = new Button
        {
            Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Cancel"),
            MinWidth = 100
        };

        buttonPanel.Children.Add(recordButton);
        buttonPanel.Children.Add(stopButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        // ---- Lógica ----

        // Timer para actualizar nivel y duración durante la grabación.
        levelTimer.Tick += (s, ev) =>
        {
            if (recordingEngine.IsRecording)
            {
                float level = recordingEngine.GetRecordingLevel();
                double db = LinearToDb(level);
                vuMeter.SetLevels(db, db);

                var duration = recordingEngine.GetRecordedDuration();
                durationText.Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.Duration", FormatTime(duration));
            }
        };

        // Grabar
        recordButton.Click += async (s, ev) =>
        {
            if (recordingEngine.IsRecording) return;

            // Inicializar dispositivo seleccionado.
            if (deviceCombo.SelectedItem is not RecordingDeviceInfo selectedDevice)
                return;

            if (!recordingEngine.InitDevice(selectedDevice.Id))
            {
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.RecordDeviceUnavailable");
                return;
            }

            // Pedir al usuario dónde guardar el WAV.
            var topLevel = TopLevel.GetTopLevel(dialog);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.SaveRecording"),
                DefaultExtension = "wav",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Wav"))
                    {
                        Patterns = new[] { "*.wav" }
                    }
                }
            });

            if (file == null) return;

            var path = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            tempPath = path;

            // Obtener formato seleccionado.
            int sampleRate, channels;
            switch (formatCombo.SelectedIndex)
            {
                case 1: sampleRate = 44100; channels = 1; break;
                case 2: sampleRate = 48000; channels = 2; break;
                case 3: sampleRate = 48000; channels = 1; break;
                default: sampleRate = 44100; channels = 2; break;
            }

            if (!recordingEngine.StartRecording(sampleRate, channels, path))
            {
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.RecordStartError");
                return;
            }

            // Actualizar UI.
            recordButton.IsEnabled = false;
            stopButton.IsEnabled = true;
            deviceCombo.IsEnabled = false;
            formatCombo.IsEnabled = false;

            levelTimer.Start();
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.Recording");
        };

        // Detener
        stopButton.Click += (s, ev) =>
        {
            if (!recordingEngine.IsRecording) return;

            recordingEngine.StopRecording();
            levelTimer.Stop();

            resultPath = tempPath;
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.RecordingFinished");
            dialog.Close();
        };

        // Cancelar
        cancelButton.Click += (s, ev) =>
        {
            recordingEngine.StopRecording();
            levelTimer.Stop();
            resultPath = null;
            dialog.Close();
        };

        // Al cerrar la ventana, detener todo.
        dialog.Closed += (s, ev) =>
        {
            recordingEngine.StopRecording();
            levelTimer.Stop();
            recordingEngine.Dispose();
        };

        await dialog.ShowDialog(this);

        return resultPath;
    }

    // ==========================================
    // ELIMINAR Y DUPLICAR
    // ==========================================

    private void DeleteClip_Click(object? sender, RoutedEventArgs e)
    {
        DeleteSelectedClip();
    }

    private void DeleteSelectedClip()
    {
        if (_selectedTrack == null || _selectedClip == null)
            return;

        var clipName = _selectedClip.Name;
        _selectedTrack.Clips.Remove(_selectedClip);

        _selectedClip = null;
        _selectedTrack = null;
        SelectedClipPanel.IsVisible = false;

        RefreshTimeline();
        UpdateTransportState();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ClipDeleted", clipName);
    }

    private void DuplicateClip_Click(object? sender, RoutedEventArgs e)
    {
        DuplicateSelectedClip();
    }

    private void NormalizeClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedTrack == null || _selectedClip == null)
            return;

        var newGain = _engine.NormalizeClip(_selectedClip);

        if (newGain.HasValue)
        {
            // Actualizar el stream activo con la nueva ganancia.
            _engine.UpdateClipAttributes(_selectedTrack, _selectedClip);

            // Actualizar el slider de ganancia en el panel.
            SelectedGain.Value = _selectedClip.Gain;
            SelectedGainValue.Text = _selectedClip.Gain.ToString("0.00");

            RefreshTrackTimeline(_selectedTrack);
            MarkAsChanged();

            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ClipNormalized", newGain.Value);
        }
        else
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NormalizeError");
        }
    }

    private void DuplicateSelectedClip()
    {
        if (_selectedTrack == null || _selectedClip == null)
            return;

        var original = _selectedClip;

        var copy = new AudioClip
        {
            FilePath = original.FilePath,
            Name = original.Name + " (copia)",
            StartTime = original.EndTime,
            SourceOffset = original.SourceOffset,
            Duration = original.Duration,
            SourceFileDuration = original.SourceFileDuration,
            Gain = original.Gain,
            Row = original.Row,
            FadeIn = original.FadeIn,
            FadeOut = original.FadeOut
        };

        _selectedTrack.Clips.Add(copy);

        // Seleccionar la copia recién creada.
        _selectedClip = copy;
        UpdateSelectedClipEditor();

        RefreshTimeline();
        UpdateTransportState();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.ClipDuplicated");
    }

    // ==========================================
    // CORTAR / SPLIT CLIP
    // ==========================================

    private void SplitClip_Click(object? sender, RoutedEventArgs e)
    {
        SplitSelectedClip();
    }

    private void SplitSelectedClip()
    {
        if (_selectedTrack == null || _selectedClip == null)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.SelectClipToSplit");
            return;
        }

        var clip = _selectedClip;
        var cursorPosition = _engine.GetPositionSeconds();

        // Verificar que el cursor esté dentro del clip.
        if (cursorPosition <= clip.StartTime || cursorPosition >= clip.EndTime)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.CursorOutsideClip");
            return;
        }

        // Calcular el punto de corte relativo al inicio del clip.
        double splitPoint = cursorPosition - clip.StartTime;

        // Verificar que el corte no sea demasiado pequeño.
        if (splitPoint < 0.01 || (clip.Duration - splitPoint) < 0.01)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.SplitNearEdge");
            return;
        }

        // Guardar valores originales.
        double originalDuration = clip.Duration;
        double originalFadeOut = clip.FadeOut;

        // MODIFICAR el primer clip PRIMERO.
        clip.Duration = splitPoint;
        clip.FadeOut = 0;

        // Crear el segundo clip usando los valores del primer clip YA MODIFICADO.
        // Esto garantiza que StartTime y SourceOffset sean exactamente correctos.
        var secondClip = new AudioClip
        {
            FilePath = clip.FilePath,
            Name = clip.Name,
            StartTime = clip.StartTime + clip.Duration,       // EndTime exacto del primer clip
            SourceOffset = clip.SourceOffset + clip.Duration,  // Continuación exacta del audio
            Duration = originalDuration - splitPoint,
            SourceFileDuration = clip.SourceFileDuration,
            Gain = clip.Gain,
            Row = clip.Row,
            FadeIn = 0,
            FadeOut = originalFadeOut
        };

        // Agregar el segundo clip a la pista.
        _selectedTrack.Clips.Add(secondClip);

        // Actualizar el editor.
        UpdateSelectedClipEditor();

        RefreshTimeline();
        UpdateTransportState();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ClipSplit", FormatTime(cursorPosition));
    }

    private void DeleteTrack(ProjectTrack track)
    {
        if (track == null) return;

        // Si la pista eliminada tiene el clip seleccionado, limpiar selección.
        if (_selectedTrack?.Id == track.Id)
        {
            _selectedTrack = null;
            _selectedClip = null;
            SelectedClipPanel.IsVisible = false;
        }

        var trackName = track.Name;

        _engine.Project.Tracks.Remove(track);

        // Limpiar diccionarios de UI.
        _trackHeaders.Remove(track.Id);
        _trackCanvases.Remove(track.Id);
        _trackCursors.Remove(track.Id);
        _trackScrollViewers.Remove(track.Id);

        _trackLoopChecks.Remove(track.Id);
        _trackLoopStartBoxes.Remove(track.Id);
        _trackLoopEndBoxes.Remove(track.Id);
        _tempMarkers.Remove(track.Id);

        // Reconstruir toda la UI de pistas.
        TracksPanel.Children.Clear();

        foreach (var t in _engine.Project.Tracks)
        {
            AddTrackUi(t);
        }

        RefreshTimeline();
        UpdateTransportState();
        MarkAsChanged();

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.TrackDeleted", trackName);
    }

    // ==========================================
    // ATAJOS DE TECLADO
    // ==========================================

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // No procesar atajos si el usuario está escribiendo en un TextBox.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.S:
                    await SaveProjectInternal();
                    e.Handled = true;
                    break;

                case Key.O:
                    OpenProject_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Key.N:
                    NewProject_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Key.E:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        ExportSelection_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        Export_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                    break;

                case Key.D:
                    if (_selectedClip != null)
                    {
                        DuplicateSelectedClip();
                        e.Handled = true;
                    }
                    break;

                case Key.B:
                    if (_selectedClip != null)
                    {
                        SplitSelectedClip();
                        e.Handled = true;
                    }
                    break;

                case Key.R:
                    Record_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Space:
                    if (_engine.IsPlaying)
                        Pause_Click(this, new RoutedEventArgs());
                    else
                        Play_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Key.Delete:
                    {
                        // Prioridad: si hay un clip seleccionado, se elimina el clip.
                        // Si no, pero hay una selección (rango de loop), se elimina la selección.
                        if (_selectedClip != null)
                        {
                            DeleteSelectedClip();
                            e.Handled = true;
                        }
                        else
                        {
                            var (selStart, selEnd) = GetEffectiveLoopRange();
                            if (selStart >= 0 && selEnd > selStart)
                            {
                                await ConfirmAndDeleteSelection();
                                e.Handled = true;
                            }
                        }
                        break;
                    }

                case Key.Home:
                    _engine.Seek(0);
                    UpdatePositionUi();
                    UpdatePlaybackCursor();
                    e.Handled = true;
                    break;

                case Key.End:
                    _engine.Seek(_engine.DurationSeconds);
                    UpdatePositionUi();
                    UpdatePlaybackCursor();
                    e.Handled = true;
                    break;

                case Key.M:
                    AddMarker_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
    }

    // ==========================================
    // AGREGAR PISTAS Y CLIPS
    // ==========================================

    private void AddTrack_Click(object? sender, RoutedEventArgs e)
    {
        var track = _engine.AddTrack();
        AddTrackUi(track);
        UpdateTransportState();
        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.TrackAdded", track.Name);
        MarkAsChanged();
    }

    private async Task AddClipAsync(ProjectTrack track)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = WaveLocalizationService.Format("Loc.RMWave.Dialog.AddClips", track.Name),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Audio"))
                    {
                        Patterns = new[] { "*.wav", "*.mp3", "*.ogg" }
                    },
                    new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.All"))
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count == 0) return;

            var addedCount = 0;

            foreach (var file in files)
            {
                var path = file.Path?.LocalPath;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var clip = _engine.AddClip(track.Id, path);

                if (clip != null)
                {
                    addedCount++;

                    EnsureWaveformForClip(path);
                }
                else
                {
                    StatusText.Text = WaveLocalizationService.Format(
                        "Loc.RMWave.Status.AddClipError",
                        System.IO.Path.GetFileName(path),
                        Bass.LastError);
                }
            }

            if (addedCount > 0)
            {
                UpdateTrackHeader(track);
                RefreshTimeline();
                UpdateTransportState();
                MarkAsChanged();
                StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ClipsAdded", addedCount, track.Name);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.GenericError", ex.Message);
        }
    }

    private void EnsureWaveformForClip(string filePath)
    {
        if (_waveformCache.GetPeaks(filePath) == null)
        {
            var localPath = filePath;
            Task.Run(async () =>
            {
                try
                {
                    var peaks = _waveformCache.CalculatePeaks(localPath);
                    if (peaks != null)
                    {
                        _waveformCache.CachePeaks(localPath, peaks);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try { RefreshTimeline(); }
                            catch { }
                        });
                    }
                }
                catch { }
            });
        }
    }

    // ==========================================
    // TIMELINE VISUAL
    // ==========================================

    private void AddTrackUi(ProjectTrack track)
    {
        var root = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var panel = new StackPanel { Spacing = 6 };

        // Header.
        var header = new TextBlock
        {
            Text = FormatTrackHeader(track),
            FontWeight = FontWeight.Bold
        };
        _trackHeaders[track.Id] = header;
        panel.Children.Add(header);

        // ==========================================
        // FILA SUPERIOR: Controles de edición
        // ==========================================
        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        // 1. Agregar Clip
        var addClipButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.PlaylistPlus, Width = 16, Height = 16 },
            Width = 36,
            Height = 30
        };
        ToolTip.SetTip(addClipButton, WaveLocalizationService.GetString("Loc.RMWave.Track.AddClip"));
        addClipButton.Click += async (s, e) => await AddClipAsync(track);
        topRow.Children.Add(addClipButton);

        // 2. Agregar Fila
        var addRowButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Plus, Width = 16, Height = 16 },
            Width = 36,
            Height = 30
        };
        ToolTip.SetTip(addRowButton, WaveLocalizationService.GetString("Loc.RMWave.Track.AddRow"));
        addRowButton.Click += (s, e) =>
        {
            track.RowCount++;
            UpdateTrackHeader(track);
            RefreshTrackTimeline(track);
            MarkAsChanged();
        };
        topRow.Children.Add(addRowButton);

        // 3. Eliminar Fila
        var removeRowButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Minus, Width = 16, Height = 16 },
            Width = 36,
            Height = 30
        };
        ToolTip.SetTip(removeRowButton, WaveLocalizationService.GetString("Loc.RMWave.Track.RemoveRow"));
        removeRowButton.Click += (s, e) =>
        {
            if (track.RowCount > 1)
            {
                track.RowCount--;
                foreach (var clip in track.Clips)
                {
                    if (clip.Row >= track.RowCount)
                        clip.Row = track.RowCount - 1;
                }
                UpdateTrackHeader(track);
                RefreshTrackTimeline(track);
                MarkAsChanged();
            }
        };
        topRow.Children.Add(removeRowButton);

        // 4. Mute
        var muteCheck = new CheckBox { Content = WaveLocalizationService.GetString("Loc.RMWave.Track.Mute"), IsChecked = track.Mute };
        muteCheck.IsCheckedChanged += (s, e) =>
        {
            _engine.SetTrackMute(track, muteCheck.IsChecked == true);
            MarkAsChanged();
        };
        topRow.Children.Add(muteCheck);

        // 5. Solo
        var soloCheck = new CheckBox { Content = WaveLocalizationService.GetString("Loc.RMWave.Track.Solo"), IsChecked = track.Solo };
        soloCheck.IsCheckedChanged += (s, e) =>
        {
            _engine.SetTrackSolo(track, soloCheck.IsChecked == true);
            MarkAsChanged();
        };
        topRow.Children.Add(soloCheck);

        // 6. Volumen
        topRow.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Track.Volume"), VerticalAlignment = VerticalAlignment.Center });
        var volumeKnob = new AudioKnob
        {
            Minimum = 0,
            Maximum = 1,
            Value = track.Volume,
            KnobSize = 32,
            Label = WaveLocalizationService.GetString("Loc.RMWave.Track.Volume"),
            ValueFormat = "0.00"
        };
        volumeKnob.ValueChanged += (val) =>
        {
            _engine.SetTrackVolume(track, (float)val);
            MarkAsChanged();
        };
        topRow.Children.Add(volumeKnob);

        // 7. Pan
        topRow.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Track.Pan"), VerticalAlignment = VerticalAlignment.Center });
        var panKnob = new AudioKnob
        {
            Minimum = -1,
            Maximum = 1,
            Value = track.Pan,
            KnobSize = 32,
            Label = WaveLocalizationService.GetString("Loc.RMWave.Track.Pan"),
            ValueFormat = "0.00",
            ValueArcColor = new SolidColorBrush(Color.FromRgb(180, 120, 220))
        };
        panKnob.ValueChanged += (val) =>
        {
            _engine.SetTrackPan(track, (float)val);
            MarkAsChanged();
        };
        topRow.Children.Add(panKnob);

        // 8. Ecualizador
        topRow.Children.Add(new Border { Width = 10 }); // separador

        // Bass
        topRow.Children.Add(new TextBlock { Text = "B", VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Opacity = 0.7 });
        var bassKnob = new AudioKnob
        {
            Minimum = -12,
            Maximum = 12,
            Value = track.EqBass,
            KnobSize = 32,
            Label = WaveLocalizationService.GetString("Loc.RMWave.Track.Bass"),
            ValueFormat = "+0;-0;0",
            ArcThickness = 2.5,
            ValueArcColor = new SolidColorBrush(Color.FromRgb(220, 140, 60))
        };
        bassKnob.ValueChanged += (val) =>
        {
            _engine.SetTrackEq(track, (float)val, track.EqMid, track.EqTreble);
            MarkAsChanged();
        };
        topRow.Children.Add(bassKnob);

        // Mid
        topRow.Children.Add(new TextBlock { Text = "M", VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Opacity = 0.7 });
        var midKnob = new AudioKnob
        {
            Minimum = -12,
            Maximum = 12,
            Value = track.EqMid,
            KnobSize = 32,
            Label = WaveLocalizationService.GetString("Loc.RMWave.Track.Mid"),
            ValueFormat = "+0;-0;0",
            ArcThickness = 2.5,
            ValueArcColor = new SolidColorBrush(Color.FromRgb(220, 140, 60))
        };
        midKnob.ValueChanged += (val) =>
        {
            _engine.SetTrackEq(track, track.EqBass, (float)val, track.EqTreble);
            MarkAsChanged();
        };
        topRow.Children.Add(midKnob);

        // Treble
        topRow.Children.Add(new TextBlock { Text = "T", VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Opacity = 0.7 });
        var trebleKnob = new AudioKnob
        {
            Minimum = -12,
            Maximum = 12,
            Value = track.EqTreble,
            KnobSize = 32,
            Label = WaveLocalizationService.GetString("Loc.RMWave.Track.Treble"),
            ValueFormat = "+0;-0;0",
            ArcThickness = 2.5,
            ValueArcColor = new SolidColorBrush(Color.FromRgb(220, 140, 60))
        };
        trebleKnob.ValueChanged += (val) =>
        {
            _engine.SetTrackEq(track, track.EqBass, track.EqMid, (float)val);
            MarkAsChanged();
        };
        topRow.Children.Add(trebleKnob);

        panel.Children.Add(topRow);

        // ==========================================
        // FILA MEDIA: Timeline con clips
        // ==========================================
        var canvasHeight = GetCanvasHeight(track);

        var canvas = new Canvas
        {
            Height = canvasHeight,
            Background = ThemeManager.Current.GetCanvasBrush()
        };

        var cursor = new Line
        {
            Stroke = Brushes.Red,
            StrokeThickness = 1,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, canvasHeight),
            IsHitTestVisible = false
        };

        _trackCanvases[track.Id] = canvas;
        _trackCursors[track.Id] = cursor;

        canvas.PointerPressed += (s, e) =>
        {
            if (_isDragging) return;

            var pos = e.GetPosition(canvas);
            double time = Math.Max(0, pos.X / track.Zoom);

            // Buscar un marcador cercano a la posición del click.
            TimelineMarker? markerNear = null;
            foreach (var marker in _engine.Project.Markers)
            {
                var markerX = marker.Time * track.Zoom;
                if (Math.Abs(pos.X - markerX) < 6)
                {
                    markerNear = marker;
                    break;
                }
            }

            // CLICK DERECHO: menú contextual sobre marcador o sobre la selección.
            var props = e.GetCurrentPoint(canvas).Properties;
            if (props.IsRightButtonPressed)
            {
                if (markerNear != null)
                {
                    ShowMarkerContextMenu(markerNear, canvas);
                    e.Handled = true;
                }
                else if (IsInsideSelection(time))
                {
                    ShowSelectionContextMenu(canvas);
                    e.Handled = true;
                }
                return;
            }

            // CLICK IZQUIERDO cerca de un marcador: seek a esa posición.
            if (markerNear != null)
            {
                _clickTimer?.Stop();
                _engine.Seek(markerNear.Time);
                UpdatePositionUi();
                UpdatePlaybackCursor();
                return;
            }

            if (e.ClickCount == 2)
            {
                // Doble click: marcar inicio/fin de loop.
                _clickTimer?.Stop();
                HandleLoopDoubleClick(track, time);

                // Limpiar selección de clip para que Delete actúe sobre la selección.
                ClearSelection();

                _tempMarkers[track.Id] = time;

                RefreshTrackTimeline(track);
            }
            else if (e.ClickCount == 1)
            {
                // Click simple: retrasar para ver si es doble click.
                _pendingClickTrackId = track.Id;
                _pendingClickTime = time;
                _clickTimer?.Stop();
                _clickTimer?.Start();
            }
        };

        var timelineScroll = new ScrollViewer
        {
            Content = canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _trackScrollViewers[track.Id] = timelineScroll;

        panel.Children.Add(timelineScroll);

        // ==========================================
        // FILA INFERIOR: Zoom y Loop
        // ==========================================
        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        // Zoom
        bottomRow.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Track.Zoom"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var zoomSlider = new Slider
        {
            Minimum = 10,
            Maximum = 300,
            Value = track.Zoom,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center
        };
        zoomSlider.ValueChanged += (s, e) =>
        {
            track.Zoom = e.NewValue;
            RefreshTrackTimeline(track);
            UpdatePlaybackCursor();
        };
        bottomRow.Children.Add(zoomSlider);

        // Separador visual.
        bottomRow.Children.Add(new Border { Width = 20 });

        // Loop
        var loopCheck = new CheckBox
        {
            Content = WaveLocalizationService.GetString("Loc.RMWave.Track.Loop"),
            IsChecked = track.LoopEnabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        loopCheck.IsCheckedChanged += (s, e) =>
        {
            track.LoopEnabled = loopCheck.IsChecked == true;
            RefreshTrackTimeline(track);
        };
        bottomRow.Children.Add(loopCheck);

        // Loop Start
        bottomRow.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Track.LoopIn"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        var loopStartBox = new TextBox
        {
            Width = 55,
            VerticalAlignment = VerticalAlignment.Center,
            Text = FormatLoopTime(track.LoopStart)
        };
        loopStartBox.LostFocus += (s, e) =>
        {
            track.LoopStart = ParseLoopTime(loopStartBox.Text ?? "");
            loopStartBox.Text = FormatLoopTime(track.LoopStart);
            RefreshTrackTimeline(track);
            MarkAsChanged();
        };
        bottomRow.Children.Add(loopStartBox);

        var captureStartBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.ArrowDownBold, Width = 14, Height = 14 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(captureStartBtn, WaveLocalizationService.GetString("Loc.RMWave.Track.CaptureLoopStart"));
        captureStartBtn.Click += (s, e) =>
        {
            track.LoopStart = _engine.GetPositionSeconds();
            loopStartBox.Text = FormatLoopTime(track.LoopStart);
            RefreshTrackTimeline(track);
            MarkAsChanged();
        };
        bottomRow.Children.Add(captureStartBtn);

        // Loop End
        bottomRow.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Track.LoopOut"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        var loopEndBox = new TextBox
        {
            Width = 55,
            VerticalAlignment = VerticalAlignment.Center,
            Text = FormatLoopTime(track.LoopEnd)
        };
        loopEndBox.LostFocus += (s, e) =>
        {
            track.LoopEnd = ParseLoopTime(loopEndBox.Text ?? "");
            loopEndBox.Text = FormatLoopTime(track.LoopEnd);
            RefreshTrackTimeline(track);
            MarkAsChanged();
        };
        bottomRow.Children.Add(loopEndBox);

        var captureEndBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.ArrowDownBold, Width = 14, Height = 14 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(captureEndBtn, WaveLocalizationService.GetString("Loc.RMWave.Track.CaptureLoopEnd"));
        captureEndBtn.Click += (s, e) =>
        {
            track.LoopEnd = _engine.GetPositionSeconds();
            loopEndBox.Text = FormatLoopTime(track.LoopEnd);
            RefreshTrackTimeline(track);
            MarkAsChanged();
        };
        bottomRow.Children.Add(captureEndBtn);

        // Guardar referencias para actualizar desde el doble click.
        _trackLoopChecks[track.Id] = loopCheck;
        _trackLoopStartBoxes[track.Id] = loopStartBox;
        _trackLoopEndBoxes[track.Id] = loopEndBox;

        // Botón eliminar pista.
        var deleteTrackButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 16, Height = 16 },
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            Width = 36,
            Height = 30,
            Margin = new Thickness(12, 0, 0, 0)
        };
        ToolTip.SetTip(deleteTrackButton, WaveLocalizationService.GetString("Loc.RMWave.Track.DeleteTrack"));
        deleteTrackButton.Click += (s, e) => DeleteTrack(track);
        bottomRow.Children.Add(deleteTrackButton);

        panel.Children.Add(bottomRow);

        root.Child = panel;
        TracksPanel.Children.Add(root);

        RefreshTrackTimeline(track);
    }

    private double GetCanvasHeight(ProjectTrack track)
    {
        return Math.Max(TimelineHeight, TimeRulerHeight + track.RowCount * RowHeight + 10);
    }

    private void RefreshTimeline()
    {
        foreach (var track in _engine.Project.Tracks)
        {
            RefreshTrackTimeline(track);
        }
        UpdatePlaybackCursor();
    }

    private void RefreshTrackTimeline(ProjectTrack track)
    {
        if (!_trackCanvases.TryGetValue(track.Id, out var canvas))
            return;

        canvas.Children.Clear();

        var canvasHeight = GetCanvasHeight(track);
        canvas.Height = canvasHeight;

        var width = Math.Max(
            200,
            _engine.DurationSeconds * track.Zoom + 100);

        canvas.Width = width;

        // 1) Líneas de separación entre filas.
        for (int row = 1; row < track.RowCount; row++)
        {
            var lineY = TimeRulerHeight + 5 + row * RowHeight;

            var separator = new Line
            {
                //Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Stroke = ThemeManager.Current.GetGridLineBrush(),
                StrokeThickness = 1,
                StartPoint = new Point(0, lineY),
                EndPoint = new Point(width, lineY),
                IsHitTestVisible = false
            };

            canvas.Children.Add(separator);
        }

        // NUEVO: Resaltar la zona de loop si está activo.
        if (track.LoopEnabled && track.LoopEnd > track.LoopStart + 0.01)
        {
            var loopStartX = track.LoopStart * track.Zoom;
            var loopEndX = track.LoopEnd * track.Zoom;
            var loopWidth = Math.Max(1, loopEndX - loopStartX);

            // Rectángulo semitransparente cubriendo el rango del loop.
            var loopRect = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = loopWidth,
                Height = canvasHeight,
                Fill = ThemeManager.Current.GetLoopFillBrush(),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(loopRect, loopStartX);
            Canvas.SetTop(loopRect, 0);
            canvas.Children.Add(loopRect);

            // Línea vertical en el inicio del loop.
            var loopStartLine = new Line
            {
                Stroke = ThemeManager.Current.GetLoopBorderBrush(),
                StrokeThickness = 2,
                StartPoint = new Point(loopStartX, 0),
                EndPoint = new Point(loopStartX, canvasHeight),
                IsHitTestVisible = false
            };
            canvas.Children.Add(loopStartLine);

            // Línea vertical en el fin del loop.
            var loopEndLine = new Line
            {
                Stroke = ThemeManager.Current.GetLoopBorderBrush(),
                StrokeThickness = 2,
                StartPoint = new Point(loopEndX, 0),
                EndPoint = new Point(loopEndX, canvasHeight),
                IsHitTestVisible = false
            };
            canvas.Children.Add(loopEndLine);
        }

        // 2) Clips.
        foreach (var clip in track.Clips)
        {
            var isSelected = _selectedClip?.Id == clip.Id;
            var clipWidth = Math.Max(2, clip.Duration * track.Zoom);
            var clipTop = TimeRulerHeight + 5 + clip.Row * RowHeight;

            var clipBorder = new Border
            {
                Width = clipWidth,
                Height = ClipHeight,
                BorderBrush = isSelected ? Brushes.Yellow : Brushes.Black,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Tag = (track, clip),
                //Background = new SolidColorBrush(Color.FromRgb(30, 40, 50))
                Background = ThemeManager.Current.GetClipBrush()
            };

            var grid = new Grid();

            var waveformGeometry = CreateWaveformGeometry(clip, clipWidth, ClipHeight);
            if (waveformGeometry != null)
            {
                var waveformPath = new Avalonia.Controls.Shapes.Path
                {
                    //Stroke = new SolidColorBrush(Color.FromRgb(70, 180, 220)),
                    Stroke = ThemeManager.Current.GetWaveformBrush(),
                    StrokeThickness = 1,
                    Data = waveformGeometry,
                    Stretch = Stretch.None,
                    Width = clipWidth,
                    Height = ClipHeight,
                    IsHitTestVisible = false
                };
                grid.Children.Add(waveformPath);
            }

            var fadeGeometry = CreateFadeGeometry(clip, clipWidth, ClipHeight, track.Zoom);
            if (fadeGeometry != null)
            {
                var fadePath = new Avalonia.Controls.Shapes.Path
                {
                    //Fill = new SolidColorBrush(Color.FromArgb(70, 255, 165, 0)),
                    Fill = ThemeManager.Current.GetFadeBrush(),
                    Data = fadeGeometry,
                    Stretch = Stretch.None,
                    Width = clipWidth,
                    Height = ClipHeight,
                    IsHitTestVisible = false
                };
                grid.Children.Add(fadePath);
            }

            var label = new TextBlock
            {
                Text = clip.Name,
                //Foreground = Brushes.White,
                Foreground = ThemeManager.Current.GetClipTextBrush(),
                Margin = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Opacity = 0.9,
                FontWeight = FontWeight.SemiBold
            };
            grid.Children.Add(label);

            clipBorder.Child = grid;

            clipBorder.PointerPressed += OnClipPointerPressed;
            clipBorder.PointerMoved += OnClipPointerMoved;
            clipBorder.PointerReleased += OnClipPointerReleased;

            Canvas.SetLeft(clipBorder, clip.StartTime * track.Zoom);
            Canvas.SetTop(clipBorder, clipTop);

            canvas.Children.Add(clipBorder);
        }

        // 3) Regla de tiempo DESPUÉS de los clips para que quede encima.
        double timeInterval = GetTimeInterval(track.Zoom);
        double maxTime = width / track.Zoom;

        for (double t = timeInterval; t <= maxTime; t += timeInterval)
        {
            double x = t * track.Zoom;

            var timeLine = new Line
            {
                //Stroke = new SolidColorBrush(Color.FromArgb(50, 150, 150, 150)),
                Stroke = ThemeManager.Current.GetRulerLineBrush(),
                StrokeThickness = 1,
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, canvasHeight),
                IsHitTestVisible = false
            };
            canvas.Children.Add(timeLine);

            var timeLabel = new TextBlock
            {
                Text = FormatTimeShort(t),
                //Foreground = new SolidColorBrush(Color.FromArgb(140, 160, 160, 160)),
                Foreground = ThemeManager.Current.GetRulerTextBrush(),
                FontSize = 9,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(timeLabel, x + 2);
            Canvas.SetTop(timeLabel, 2);
            canvas.Children.Add(timeLabel);
        }

        // Línea de referencia temporal (click simple en el timeline).
        if (_tempMarkers.TryGetValue(track.Id, out var markerTime) && markerTime >= 0)
        {
            var markerX = markerTime * track.Zoom;

            var markerLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(180, 220, 220, 220)),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                StartPoint = new Point(markerX, 0),
                EndPoint = new Point(markerX, canvasHeight),
                IsHitTestVisible = false
            };
            canvas.Children.Add(markerLine);
        }

        // Marcadores del timeline.
        foreach (var marker in _engine.Project.Markers)
        {
            var markerX = marker.Time * track.Zoom;

            if (markerX < 0 || markerX > width) continue;

            // Línea vertical del marcador.
            var markerLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 233, 30, 99)),
                StrokeThickness = 1.5,
                StartPoint = new Point(markerX, TimeRulerHeight),
                EndPoint = new Point(markerX, canvasHeight),
                IsHitTestVisible = false
            };
            canvas.Children.Add(markerLine);

            // Etiqueta del marcador (en el área de la regla).
            var markerLabel = new TextBlock
            {
                Text = marker.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(233, 30, 99)),
                FontSize = 9,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(markerLabel, markerX + 3);
            Canvas.SetTop(markerLabel, 2);
            canvas.Children.Add(markerLabel);
        }

        // 4) Cursor de reproducción al final para que quede encima de todo.
        if (_trackCursors.TryGetValue(track.Id, out var cursor))
        {
            var x = _engine.GetPositionSeconds() * track.Zoom;
            cursor.StartPoint = new Point(x, 0);
            cursor.EndPoint = new Point(x, canvasHeight);
            canvas.Children.Add(cursor);
        }
    }

    private StreamGeometry? CreateWaveformGeometry(AudioClip clip, double width, double height)
    {
        try
        {
            var peaks = _waveformCache.GetPeaks(clip.FilePath);
            if (peaks == null || peaks.Length == 0 || clip.SourceFileDuration <= 0)
                return null;

            var midY = height / 2;
            double peaksPerSecond = peaks.Length / clip.SourceFileDuration;

            int startPeakIndex = (int)(clip.SourceOffset * peaksPerSecond);
            int endPeakIndex = (int)((clip.SourceOffset + clip.Duration) * peaksPerSecond);

            startPeakIndex = Math.Max(0, Math.Min(startPeakIndex, peaks.Length - 1));
            endPeakIndex = Math.Max(0, Math.Min(endPeakIndex, peaks.Length));

            int peaksToDraw = endPeakIndex - startPeakIndex;
            if (peaksToDraw <= 0) return null;

            double pixelsPerPeak = width / peaksToDraw;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                if (pixelsPerPeak < 1.0)
                {
                    int peaksPerPixel = (int)Math.Ceiling(1.0 / pixelsPerPeak);
                    int numLines = (int)Math.Ceiling((double)peaksToDraw / peaksPerPixel);
                    double xScale = numLines > 0 ? width / numLines : 1;
                    int lineIndex = 0;

                    for (int i = 0; i < peaksToDraw; i++)
                    {
                        float maxInBlock = 0;
                        int limit = Math.Min(peaksPerPixel, peaksToDraw - i);
                        for (int j = 0; j < limit; j++)
                        {
                            float val = peaks[startPeakIndex + i + j];
                            if (val > maxInBlock) maxInBlock = val;
                        }

                        double lineHeight = maxInBlock * midY;
                        double x = lineIndex * xScale;

                        ctx.BeginFigure(new Point(x, midY - lineHeight), false);
                        ctx.LineTo(new Point(x, midY + lineHeight));

                        i += limit - 1;
                        lineIndex++;
                    }
                }
                else
                {
                    for (int i = 0; i < peaksToDraw; i++)
                    {
                        float peak = peaks[startPeakIndex + i];
                        double x = (double)i / peaksToDraw * width;
                        double lineHeight = peak * midY;

                        ctx.BeginFigure(new Point(x, midY - lineHeight), false);
                        ctx.LineTo(new Point(x, midY + lineHeight));
                    }
                }
            }

            return geometry;
        }
        catch
        {
            return null;
        }
    }

    private StreamGeometry? CreateFadeGeometry(AudioClip clip, double width, double height, double zoom)
    {
        if (clip.FadeIn <= 0 && clip.FadeOut <= 0)
            return null;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Fade In: triángulo que muestra el volumen subiendo de 0 a 1.
            if (clip.FadeIn > 0)
            {
                double fadeInWidth = Math.Min(clip.FadeIn * zoom, width);

                ctx.BeginFigure(new Point(0, height), true);
                ctx.LineTo(new Point(fadeInWidth, 0));
                ctx.LineTo(new Point(fadeInWidth, height));
                ctx.EndFigure(true);
            }

            // Fade Out: triángulo que muestra el volumen bajando de 1 a 0.
            if (clip.FadeOut > 0)
            {
                double fadeOutWidth = Math.Min(clip.FadeOut * zoom, width);
                double fadeOutStart = Math.Max(0, width - fadeOutWidth);

                ctx.BeginFigure(new Point(fadeOutStart, 0), true);
                ctx.LineTo(new Point(width, height));
                ctx.LineTo(new Point(fadeOutStart, height));
                ctx.EndFigure(true);
            }
        }

        return geometry;
    }

    // ==========================================
    // DRAG & DROP
    // ==========================================

    private void OnClipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not (ProjectTrack track, AudioClip clip)) return;

        SelectClip(track, clip);

        if (!_trackCanvases.TryGetValue(track.Id, out var canvas)) return;

        // Cancelar el timer de click simple si el usuario hace click en un clip.
        _clickTimer?.Stop();

        _isDragging = true;
        _draggedTrack = track;
        _draggedClip = clip;
        _draggedBorder = border;

        _dragStartPoint = e.GetPosition(canvas);
        _dragStartClipStartTime = clip.StartTime;
        _dragStartClipRow = clip.Row;

        e.Pointer.Capture(border);
        e.Handled = true;

    }

    private void OnClipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || sender is not Border border || border != _draggedBorder) return;
        if (_draggedTrack == null || _draggedClip == null) return;
        if (!_trackCanvases.TryGetValue(_draggedTrack.Id, out var canvas)) return;

        var currentPoint = e.GetPosition(canvas);

        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaTime = deltaX / _draggedTrack.Zoom;
        var newStartTime = Math.Max(0, _dragStartClipStartTime + deltaTime);

        newStartTime = ApplySnap(newStartTime, _draggedClip, _draggedTrack.Zoom);

        var deltaY = currentPoint.Y - _dragStartPoint.Y;
        var rowDelta = (int)Math.Round(deltaY / RowHeight);
        var newRow = Math.Clamp(_dragStartClipRow + rowDelta, 0, _draggedTrack.RowCount - 1);

        Canvas.SetLeft(border, newStartTime * _draggedTrack.Zoom);
        Canvas.SetTop(border, TimeRulerHeight + 5 + newRow * RowHeight);

        _draggedClip.StartTime = newStartTime;
        _draggedClip.Row = newRow;

        if (_selectedClip == _draggedClip)
        {
            SelectedStart.Text = FormatEditable(newStartTime);
        }

        e.Handled = true;
    }

    private void OnClipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || sender is not Border border || border != _draggedBorder) return;
        if (_draggedTrack == null || _draggedClip == null) return;

        e.Pointer.Capture(null);
        _isDragging = false;

        _engine.RefreshClip(_draggedTrack, _draggedClip);
        MarkAsChanged();

        RefreshTrackTimeline(_draggedTrack);

        _draggedTrack = null;
        _draggedClip = null;
        _draggedBorder = null;

        e.Handled = true;
    }

    // ==========================================
    // SNAP MAGNÉTICO
    // ==========================================

    private double ApplySnap(double newStartTime, AudioClip draggedClip, double zoom)
    {
        if (SnapCheckBox.IsChecked != true)
            return newStartTime;

        double snapThresholdSeconds = 10.0 / zoom;

        double bestSnap = newStartTime;
        double bestDistance = snapThresholdSeconds;

        double distToZero = Math.Abs(newStartTime);
        if (distToZero < bestDistance)
        {
            bestDistance = distToZero;
            bestSnap = 0;
        }

        foreach (var track in _engine.Project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Id == draggedClip.Id)
                    continue;

                double d1 = Math.Abs(newStartTime - clip.StartTime);
                if (d1 < bestDistance)
                {
                    bestDistance = d1;
                    bestSnap = clip.StartTime;
                }

                double d2 = Math.Abs(newStartTime - clip.EndTime);
                if (d2 < bestDistance)
                {
                    bestDistance = d2;
                    bestSnap = clip.EndTime;
                }

                double draggedEnd = newStartTime + draggedClip.Duration;
                double d3 = Math.Abs(draggedEnd - clip.StartTime);
                if (d3 < bestDistance)
                {
                    bestDistance = d3;
                    bestSnap = clip.StartTime - draggedClip.Duration;
                }

                double d4 = Math.Abs(draggedEnd - clip.EndTime);
                if (d4 < bestDistance)
                {
                    bestDistance = d4;
                    bestSnap = clip.EndTime - draggedClip.Duration;
                }
            }
        }

        return Math.Max(0, bestSnap);
    }

    // ==========================================
    // CURSOR DE REPRODUCCIÓN
    // ==========================================

    private void UpdatePlaybackCursor()
    {
        var position = _engine.GetPositionSeconds();

        foreach (var kvp in _trackCanvases)
        {
            var trackId = kvp.Key;

            if (!_trackCursors.TryGetValue(trackId, out var cursor))
                continue;

            var track = _engine.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null) continue;

            var x = position * track.Zoom;

            cursor.StartPoint = new Point(x, 0);
            cursor.EndPoint = new Point(x, cursor.EndPoint.Y);

            if (_trackScrollViewers.TryGetValue(trackId, out var scrollViewer))
            {
                var viewportWidth = scrollViewer.Viewport.Width;
                var scrollOffset = scrollViewer.Offset.X;

                if (x > scrollOffset + viewportWidth - 50)
                {
                    scrollViewer.Offset = new Vector(x - viewportWidth + 100, scrollViewer.Offset.Y);
                }
                else if (x < scrollOffset + 50 && scrollOffset > 0)
                {
                    scrollViewer.Offset = new Vector(Math.Max(0, x - 100), scrollViewer.Offset.Y);
                }
            }
        }
    }

    // ==========================================
    // SELECCIÓN DE CLIPS
    // ==========================================

    private void SelectClip(ProjectTrack track, AudioClip clip)
    {
        _selectedTrack = track;
        _selectedClip = clip;
        UpdateSelectedClipEditor();
        UpdateSelectionBorders();
    }

    private void UpdateSelectionBorders()
    {
        foreach (var track in _engine.Project.Tracks)
        {
            if (!_trackCanvases.TryGetValue(track.Id, out var canvas))
                continue;

            foreach (var child in canvas.Children)
            {
                if (child is Border border && border.Tag is (ProjectTrack t, AudioClip c))
                {
                    var isSelected = _selectedClip?.Id == c.Id;
                    border.BorderBrush = isSelected ? Brushes.Yellow : Brushes.Black;
                    border.BorderThickness = new Thickness(isSelected ? 2 : 1);
                }
            }
        }
    }

    private void ClearSelection()
    {
        if (_selectedClip == null)
            return;

        _selectedTrack = null;
        _selectedClip = null;
        SelectedClipPanel.IsVisible = false;
        UpdateSelectionBorders();
    }

    private void DeselectClip_Click(object? sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    // ==========================================
    // EDITOR DE CLIP SELECCIONADO
    // ==========================================

    private void UpdateSelectedClipEditor()
    {
        if (_selectedClip == null)
        {
            SelectedClipPanel.IsVisible = false;
            return;
        }

        SelectedClipPanel.IsVisible = true;
        SelectedClipName.Text = _selectedClip.Name;

        SelectedStart.Text = FormatEditable(_selectedClip.StartTime);
        SelectedOffset.Text = FormatEditable(_selectedClip.SourceOffset);
        SelectedDuration.Text = FormatEditable(_selectedClip.Duration);

        SelectedGain.Value = _selectedClip.Gain;
        SelectedGainValue.Text = _selectedClip.Gain.ToString("0.00");

        SelectedFadeIn.Text = FormatEditable(_selectedClip.FadeIn);
        SelectedFadeOut.Text = FormatEditable(_selectedClip.FadeOut);
    }

    private void SelectedEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySelectedClip();
            e.Handled = true;
        }
    }

    private void OnSelectedGainChanged(double newValue)
    {
        if (_selectedTrack == null || _selectedClip == null) return;

        _selectedClip.Gain = (float)newValue;
        SelectedGainValue.Text = newValue.ToString("0.00");
        _engine.UpdateClipAttributes(_selectedTrack, _selectedClip);
        MarkAsChanged();
    }

    private void ApplyClip_Click(object? sender, RoutedEventArgs e)
    {
        ApplySelectedClip();
    }

    private void ApplySelectedClip()
    {
        if (_selectedTrack == null || _selectedClip == null)
            return;

        if (!TryParseDouble(SelectedStart.Text, out var start) ||
            !TryParseDouble(SelectedOffset.Text, out var offset) ||
            !TryParseDouble(SelectedDuration.Text, out var duration))
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.InvalidNumbers");
            return;
        }

        start = Math.Max(0, start);

        var maxOffset = Math.Max(0, _selectedClip.SourceFileDuration);
        offset = Math.Clamp(offset, 0, maxOffset);

        var maxDuration = Math.Max(0, _selectedClip.SourceFileDuration - offset);
        duration = Math.Clamp(duration, 0, maxDuration);

        _selectedClip.StartTime = start;
        _selectedClip.SourceOffset = offset;
        _selectedClip.Duration = duration;

        if (TryParseDouble(SelectedFadeIn.Text, out var fadeIn))
            _selectedClip.FadeIn = Math.Clamp(fadeIn, 0, duration);

        if (TryParseDouble(SelectedFadeOut.Text, out var fadeOut))
            _selectedClip.FadeOut = Math.Clamp(fadeOut, 0, duration);

        _engine.RefreshClip(_selectedTrack, _selectedClip);

        UpdateSelectedClipEditor();
        UpdateTrackHeader(_selectedTrack);
        RefreshTimeline();
        UpdateTransportState();

        if (_engine.DurationSeconds <= 0)
        {
            _timer.Stop();
            _engine.Stop();
            UpdatePositionUi();
            UpdatePlaybackCursor();
        }

        StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.ClipUpdated");
        MarkAsChanged();
    }

    // ==========================================
    // ACTUALIZAR UI
    // ==========================================

    private void UpdateTrackHeader(ProjectTrack track)
    {
        if (_trackHeaders.TryGetValue(track.Id, out var header))
        {
            header.Text = FormatTrackHeader(track);
        }
    }

    private void UpdateTransportState()
    {
        var duration = _engine.DurationSeconds;

        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(0, duration);
        if (PositionSlider.Value > PositionSlider.Maximum)
            PositionSlider.Value = PositionSlider.Maximum;
        _updatingSlider = false;

        DurationText.Text = FormatTime(duration);

        var hasAudio = duration > 0;
        PlayButton.IsEnabled = hasAudio;
        PauseButton.IsEnabled = hasAudio;
        StopButton.IsEnabled = hasAudio;
    }

    // ==========================================
    // EXPORTAR
    // ==========================================

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (_engine.Project.Duration <= 0)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NoAudioToExport");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.ExportMix"),
            DefaultExtension = "wav",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Wav"))
                {
                    Patterns = new[] { "*.wav" }
                },
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Mp3"))
                {
                    Patterns = new[] { "*.mp3" }
                }
            }
        });

        if (file == null) return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;
        StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.ExportingMix");

        var extension = System.IO.Path.GetExtension(path).ToLower();

        try
        {
            if (extension == ".mp3")
            {
                await _engine.RenderToMp3Async(
                    path,
                    progress => Dispatcher.UIThread.Post(() => ExportProgress.Value = progress * 100),
                    status => Dispatcher.UIThread.Post(() => StatusText.Text = WaveLocalizationService.TranslateExportStatus(status)));
            }
            else
            {
                await _engine.RenderToFileAsync(
                    path,
                    progress => Dispatcher.UIThread.Post(() => ExportProgress.Value = progress * 100),
                    status => Dispatcher.UIThread.Post(() => StatusText.Text = WaveLocalizationService.TranslateExportStatus(status)));
            }
        }
        finally
        {
            ExportProgress.IsVisible = false;
        }
    }

    // ==========================================
    // SALIR Y ACERCA DE
    // ==========================================

    private async void Exit_Click(object? sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = await ShowUnsavedChangesDialog();

            if (result == ConfirmationResult.Cancel)
                return;

            if (result == ConfirmationResult.Save)
            {
                var saved = await SaveProjectInternal();
                if (!saved)
                    return;
            }
        }

        Close();
    }

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var buildDate = System.IO.File.GetLastWriteTime(assembly.Location);

        string versionStr = version != null
            ? $"{version.Major}.{version.Minor:D2}.{version.Build:D2}"
            : "1.00.00";

        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.AboutTitle"),
            Width = 420,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.AppName"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.Version", versionStr),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.8
        });

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.BuildDate", buildDate.ToString("yyyy-MM-dd", WaveLocalizationService.CurrentCulture)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.6,
            FontSize = 12
        });

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Description"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var okButton = new Button
        {
            Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Accept"),
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        okButton.Click += (s, ev) => dialog.Close();

        panel.Children.Add(okButton);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    // ==========================================
    // NUEVO / GUARDAR / ABRIR PROYECTO
    // ==========================================

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = await ShowUnsavedChangesDialog();

            if (result == ConfirmationResult.Cancel)
                return;

            if (result == ConfirmationResult.Save)
            {
                var saved = await SaveProjectInternal();
                if (!saved)
                    return;
            }
        }

        CreateNewProject();
    }

    private void CreateNewProject()
    {
        _timer.Stop();

        _engine.LoadProject(new Project());

        _trackHeaders.Clear();
        _trackCanvases.Clear();
        _trackCursors.Clear();
        _trackScrollViewers.Clear();
        TracksPanel.Children.Clear();

        _selectedTrack = null;
        _selectedClip = null;
        SelectedClipPanel.IsVisible = false;

        MarkAsSaved();

        _currentProjectPath = null;
        UpdateWindowTitle();

        UpdateTransportState();
        RefreshTimeline();

        StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NewProject");
    }

    private enum ConfirmationResult
    {
        Save,
        DontSave,
        Cancel
    }

    private async Task<ConfirmationResult> ShowUnsavedChangesDialog()
    {
        var result = ConfirmationResult.Cancel;

        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.UnsavedTitle"),
            Width = 440,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.UnsavedMessage"),
            TextWrapping = TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var saveButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Save"), MinWidth = 100 };
        saveButton.Click += (s, e) => { result = ConfirmationResult.Save; dialog.Close(); };

        var dontSaveButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.DontSave"), MinWidth = 100 };
        dontSaveButton.Click += (s, e) => { result = ConfirmationResult.DontSave; dialog.Close(); };

        var cancelButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Cancel"), MinWidth = 100 };
        cancelButton.Click += (s, e) => { result = ConfirmationResult.Cancel; dialog.Close(); };

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(dontSaveButton);
        buttonPanel.Children.Add(cancelButton);

        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(this);

        return result;
    }

    private async void SaveProject_Click(object? sender, RoutedEventArgs e)
    {
        await SaveProjectInternal();
    }

    private async Task<bool> SaveProjectInternal()
    {
        string? path = _currentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return false;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.SaveProjectTitle"),
                DefaultExtension = "rmw",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Project"))
                    {
                        Patterns = new[] { "*.rmw" }
                    }
                }
            });

            if (file == null) return false;

            path = file.Path.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = System.Text.Json.JsonSerializer.Serialize(_engine.Project, options);
            await File.WriteAllTextAsync(path, json);

            MarkAsSaved();
            
            _currentProjectPath = path;

            // Actualizar en recientes.
            var projectName = _engine.Project.Name;
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = System.IO.Path.GetFileNameWithoutExtension(path);
            RecentProjectsManager.AddProject(path, projectName);

            UpdateWindowTitle();

            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.ProjectSaved");
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.SaveProjectError", ex.Message);
            return false;
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.OpenProjectTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
            new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Project"))
            {
                Patterns = new[] { "*.rmw" }
            },
            new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.All"))
            {
                Patterns = new[] { "*.*" }
            }
        }
        });

        if (files.Count == 0) return;

        var path = files[0].Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        // Verificar cambios sin guardar.
        if (_hasUnsavedChanges)
        {
            var result = await ShowUnsavedChangesDialog();
            if (result == ConfirmationResult.Cancel)
                return;

            if (result == ConfirmationResult.Save)
            {
                var saved = await SaveProjectInternal();
                if (!saved)
                    return;
            }
        }

        await OpenProjectByPath(path);
    }
    
    // ==========================================
    // CAMBIOS SIN GUARDAR
    // ==========================================
    private void MarkAsChanged()
    {
        _hasUnsavedChanges = true;
        UpdateWindowTitle();
    }

    private void MarkAsSaved()
    {
        _hasUnsavedChanges = false;
        UpdateWindowTitle();
    }

    // ==========================================
    // PROYECTOS RECIENTES
    // ==========================================

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();

        if (RecentProjectsManager.RecentList.Count == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = WaveLocalizationService.GetString("Loc.RMWave.Main.NoRecentProjects"),
                IsEnabled = false
            };
            RecentMenu.Items.Add(emptyItem);
            return;
        }

        foreach (var entry in RecentProjectsManager.RecentList)
        {
            var item = new MenuItem
            {
                Header = $"{entry.Name}  —  {System.IO.Path.GetFileName(entry.Path)}",
                Tag = entry.Path
            };
            ToolTip.SetTip(item, entry.Path);

            item.Click += RecentProject_Click;
            RecentMenu.Items.Add(item);
        }

        // Separador y opción de limpiar.
        RecentMenu.Items.Add(new Separator());

        var clearItem = new MenuItem { Header = WaveLocalizationService.GetString("Loc.RMWave.Main.ClearRecentList") };
        clearItem.Click += (s, e) =>
        {
            RecentProjectsManager.Clear();
            BuildRecentMenu();
        };
        RecentMenu.Items.Add(clearItem);
    }

    private async void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string path)
            return;

        if (!System.IO.File.Exists(path))
        {
            var result = await ShowMessageBox(
                WaveLocalizationService.GetString("Loc.RMWave.Dialog.MissingProjectTitle"),
                WaveLocalizationService.Format("Loc.RMWave.Dialog.MissingProjectMessage", path));

            if (result)
            {
                RecentProjectsManager.RemoveProject(path);
                BuildRecentMenu();
            }
            return;
        }

        // Verificar cambios sin guardar.
        if (_hasUnsavedChanges)
        {
            var unsavedResult = await ShowUnsavedChangesDialog();
            if (unsavedResult == ConfirmationResult.Cancel)
                return;

            if (unsavedResult == ConfirmationResult.Save)
            {
                var saved = await SaveProjectInternal();
                if (!saved)
                    return;
            }
        }

        // Abrir el proyecto.
        await OpenProjectByPath(path);
    }

    private async Task OpenProjectByPath(string path)
    {
        try
        {
            var json = await System.IO.File.ReadAllTextAsync(path);
            var project = System.Text.Json.JsonSerializer.Deserialize<Project>(json);

            if (project == null)
            {
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.InvalidProject");
                return;
            }

            _timer.Stop();

            _engine.LoadProject(project);

            _trackHeaders.Clear();
            _trackCanvases.Clear();
            _trackCursors.Clear();
            _trackScrollViewers.Clear();
            TracksPanel.Children.Clear();

            _selectedTrack = null;
            _selectedClip = null;
            SelectedClipPanel.IsVisible = false;

            foreach (var track in _engine.Project.Tracks)
            {
                AddTrackUi(track);
            }

            _currentProjectPath = path;

            MarkAsSaved();

            // Agregar a recientes.
            var projectName = project.Name;
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = System.IO.Path.GetFileNameWithoutExtension(path);
            RecentProjectsManager.AddProject(path, projectName);
            BuildRecentMenu();

            UpdateTransportState();
            RefreshTimeline();

            // Calcular waveforms.
            foreach (var track in _engine.Project.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    EnsureWaveformForClip(clip.FilePath);
                }
            }

            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.ProjectLoaded");
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.LoadProjectError", ex.Message);
        }
    }

    private async Task<bool> ShowMessageBox(string title, string message)
    {
        bool result = false;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var yesButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Yes"), MinWidth = 80 };
        yesButton.Click += (s, e) => { result = true; dialog.Close(); };

        var noButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.No"), MinWidth = 80 };
        noButton.Click += (s, e) => { result = false; dialog.Close(); };

        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(this);

        return result;
    }

    // ==========================================
    // PROPIEDADES DEL PROYECTO
    // ==========================================
    private async void ProjectProperties_Click(object? sender, RoutedEventArgs e)
    {
        var project = _engine.Project;

        var dialog = new Window
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.ProjectPropertiesTitle"),
            Width = 480,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10
        };

        // Nombre del programa.
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.ProgramName") });
        var nameBox = new TextBox { Text = project.Name };
        panel.Children.Add(nameBox);

        // Episodio.
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Episode") });
        var episodeBox = new TextBox { Text = project.Episode };
        panel.Children.Add(episodeBox);

        // Autor.
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Author") });
        var authorBox = new TextBox { Text = project.Author };
        panel.Children.Add(authorBox);

        // Notas.
        panel.Children.Add(new TextBlock { Text = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Notes") });
        var notesBox = new TextBox
        {
            Text = project.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80
        };
        panel.Children.Add(notesBox);

        // Fechas (solo lectura).
        panel.Children.Add(new TextBlock
        {
            Text = WaveLocalizationService.Format("Loc.RMWave.Dialog.CreatedModified", project.CreatedDate.ToString("yyyy-MM-dd HH:mm", WaveLocalizationService.CurrentCulture), project.ModifiedDate.ToString("yyyy-MM-dd HH:mm", WaveLocalizationService.CurrentCulture)),
            Opacity = 0.6,
            FontSize = 11
        });

        // Botones.
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var okButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Accept"), MinWidth = 100 };
        okButton.Click += (s, ev) =>
        {
            project.Name = nameBox.Text?.Trim() ?? "";
            project.Episode = episodeBox.Text?.Trim() ?? "";
            project.Author = authorBox.Text?.Trim() ?? "";
            project.Notes = notesBox.Text?.Trim() ?? "";
            project.ModifiedDate = DateTime.Now;

            MarkAsChanged();
            UpdateWindowTitle();

            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.PropertiesUpdated");
            dialog.Close();
        };

        var cancelButton = new Button { Content = WaveLocalizationService.GetString("Loc.RMWave.Dialog.Cancel"), MinWidth = 100 };
        cancelButton.Click += (s, ev) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    // ==========================================
    // EXPORTAR SELECCIÓN
    // ==========================================

    private async void ExportSelection_Click(object? sender, RoutedEventArgs e)
    {
        await ExportSelectionAsync();
    }

    private async Task ExportSelectionAsync()
    {
        var (start, end) = GetEffectiveLoopRange();

        if (start < 0 || end <= start)
        {
            StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.NoLoopSelection");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = WaveLocalizationService.GetString("Loc.RMWave.Dialog.ExportSelectionTitle"),
            DefaultExtension = "wav",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Wav")) { Patterns = new[] { "*.wav" } },
                new FilePickerFileType(WaveLocalizationService.GetString("Loc.RMWave.FileType.Mp3")) { Patterns = new[] { "*.mp3" } }
            }
        });

        if (file == null) return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var extension = System.IO.Path.GetExtension(path).ToLower();
        bool isMp3 = extension == ".mp3";

        StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ExportingSelection", FormatTime(start), FormatTime(end));
        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;

        try
        {
            bool success;

            if (isMp3)
            {
                success = await _engine.RenderRangeToMp3Async(
                    path, start, end,
                    progress => Dispatcher.UIThread.Post(() => ExportProgress.Value = progress * 100),
                    status => Dispatcher.UIThread.Post(() => StatusText.Text = WaveLocalizationService.TranslateExportStatus(status)));
            }
            else
            {
                success = await _engine.RenderRangeToFileAsync(
                    path, start, end,
                    progress => Dispatcher.UIThread.Post(() => ExportProgress.Value = progress * 100),
                    status => Dispatcher.UIThread.Post(() => StatusText.Text = WaveLocalizationService.TranslateExportStatus(status)));
            }

            if (success)
                StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.SelectionExported", System.IO.Path.GetFileName(path));
            else
                StatusText.Text = WaveLocalizationService.GetString("Loc.RMWave.Status.SelectionExportError");
        }
        catch (Exception ex)
        {
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.ExportSelectionError", ex.Message);
        }
        finally
        {
            ExportProgress.IsVisible = false;
        }
    }

    // ==========================================
    // UTILIDADES
    // ==========================================

    // ==========================================
    // LOOP DE REPRODUCCIÓN
    // ==========================================

    // ==========================================
    // LOOP CON DOBLE CLICK EN EL TIMELINE
    // ==========================================
    private void HandleLoopDoubleClick(ProjectTrack track, double time)
    {
        // Verificar si el doble click está entre dos marcadores.
        var markers = _engine.Project.Markers.OrderBy(m => m.Time).ToList();
        TimelineMarker? prevMarker = null;
        TimelineMarker? nextMarker = null;

        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].Time <= time)
                prevMarker = markers[i];
            else
            {
                nextMarker = markers[i];
                break;
            }
        }

        if (prevMarker != null && nextMarker != null)
        {
            // Doble click entre dos marcadores: loopear esa sección.
            track.LoopStart = prevMarker.Time;
            track.LoopEnd = nextMarker.Time;
            track.LoopEnabled = true;
        }
        else if (!track.LoopEnabled)
        {
            // Primer doble click: marcar inicio.
            track.LoopStart = time;
            track.LoopEnd = time;
            track.LoopEnabled = true;
        }
        else
        {
            if (track.LoopEnd > track.LoopStart)
            {
                // Ya hay rango completo: reiniciar con nuevo inicio.
                track.LoopStart = time;
                track.LoopEnd = time;
            }
            else
            {
                // Hay inicio pero no fin.
                if (time > track.LoopStart)
                    track.LoopEnd = time;  // Marcar fin.
                else
                {
                    track.LoopStart = time;
                    track.LoopEnd = time;
                }
            }
        }

        // Actualizar los controles de loop de la pista.
        if (_trackLoopChecks.TryGetValue(track.Id, out var check))
            check.IsChecked = true;

        if (_trackLoopStartBoxes.TryGetValue(track.Id, out var startBox))
            startBox.Text = FormatLoopTime(track.LoopStart);

        if (_trackLoopEndBoxes.TryGetValue(track.Id, out var endBox))
            endBox.Text = FormatLoopTime(track.LoopEnd);

        MarkAsChanged();
    }

    private void ProcessSingleClick()
    {
        ClearSelection();

        // Limpiar markers de otras pistas.
        var keysToRemove = _tempMarkers.Keys
            .Where(k => k != _pendingClickTrackId)
            .ToList();
        foreach (var key in keysToRemove)
            _tempMarkers.Remove(key);

        // Guardar el nuevo marker.
        _tempMarkers[_pendingClickTrackId] = _pendingClickTime;

        RefreshTimeline();
    }

    /// <summary>
    /// Interpreta un texto como tiempo en segundos.
    /// Acepta: "90", "1:30", "1:30.5", "0:01:30".
    /// </summary>
    private double ParseLoopTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Trim().TrimEnd('s');

        // Número simple (segundos).
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return Math.Max(0, seconds);

        // Formato con dos puntos: mm:ss o hh:mm:ss.
        var parts = text.Split(':');
        double result = 0;
        foreach (var part in parts)
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                result = result * 60 + val;
            else
                return 0;
        }
        return Math.Max(0, result);
    }

    /// <summary>Formatea segundos como texto compacto para los campos de loop.</summary>
    private string FormatLoopTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return ts.ToString(@"h\:mm\:ss\.f");
        return ts.ToString(@"m\:ss\.f");
    }

    private void LoadAudioDevices()
    {
        _loadingDevices = true;

        // Limpiar items anteriores del menú.
        DevicesMenu.Items.Clear();

        int count = Bass.DeviceCount;
        int defaultId = -1;
        int currentDeviceId = Bass.CurrentDevice;

        var deviceItems = new List<(int Id, string Name, bool IsDefault)>();

        for (int i = 0; i < count; i++)
        {
            if (Bass.GetDeviceInfo(i, out var info))
            {
                if (info.IsEnabled && !info.IsLoopback)
                {
                    bool isCurrent = (i == currentDeviceId) || (info.IsDefault && currentDeviceId < 0);
                    deviceItems.Add((i, info.Name ?? WaveLocalizationService.Format("Loc.RMWave.Device.FallbackName", i), info.IsDefault));

                    if (isCurrent && defaultId < 0)
                        defaultId = i;
                }
            }
        }

        foreach (var (id, name, isDefault) in deviceItems)
        {
            var displayName = isDefault
                ? $"{name} ({WaveLocalizationService.GetString("Loc.RMWave.Device.Default")})"
                : name;

            var item = new MenuItem
            {
                Header = displayName,
                Tag = id
            };

            // Mostrar check si es el dispositivo actual.
            if (id == defaultId)
            {
                item.Icon = new MaterialIcon
                {
                    Kind = MaterialIconKind.Check,
                    Width = 16,
                    Height = 16
                };
            }

            item.Click += DeviceMenuItem_Click;
            DevicesMenu.Items.Add(item);
        }

        _loadingDevices = false;
    }

    private void DeviceMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_loadingDevices)
            return;

        if (sender is not MenuItem item || item.Tag is not int deviceId)
            return;

        var result = _engine.SetDevice(deviceId);

        if (result)
        {
            // Obtener el nombre del dispositivo para mostrarlo.
            Bass.GetDeviceInfo(deviceId, out var info);
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.OutputChanged", info.Name);

            // Actualizar checks: quitar el check de todos y ponerlo solo en el seleccionado.
            foreach (var menuItem in DevicesMenu.Items.OfType<MenuItem>())
            {
                if (menuItem.Tag is int id && id == deviceId)
                {
                    menuItem.Icon = new MaterialIcon
                    {
                        Kind = MaterialIconKind.Check,
                        Width = 16,
                        Height = 16
                    };
                }
                else
                {
                    menuItem.Icon = null;
                }
            }
        }
        else
        {
            Bass.GetDeviceInfo(deviceId, out var info);
            StatusText.Text = WaveLocalizationService.Format("Loc.RMWave.Status.OutputChangeError", info.Name);
        }
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            if (!double.IsNaN(parsed) && !double.IsInfinity(parsed))
            {
                value = parsed;
                return true;
            }
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            if (!double.IsNaN(parsed) && !double.IsInfinity(parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private double GetTimeInterval(double zoom)
    {
        // Elegir un intervalo que dé marcas cada ~60-150 píxeles aproximadamente.
        double[] intervals = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

        foreach (var interval in intervals)
        {
            if (interval * zoom >= 60)
                return interval;
        }
        return 600;
    }
    private static double LinearToDb(double linear)
    {
        if (linear <= 0.0001) return -60.0;
        return 20.0 * Math.Log10(linear);
    }

    private string FormatTimeShort(double seconds)
    {
        if (seconds < 60)
            return $"{seconds:F0}s";

        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return ts.ToString(@"h\:mm\:ss");
        else
            return ts.ToString(@"m\:ss");
    }

    private static string FormatEditable(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatTrackHeader(ProjectTrack track)
    {
        return $"{track.Name} | Filas: {track.RowCount} | Duración {FormatTime(track.Duration)}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.f");
    }

    // ==========================================
    // CUSTOM CHROME (barra de titulo)
    // ==========================================

    private Material.Icons.Avalonia.MaterialIcon? _maximizeIcon;

    private void InitializeCustomChrome()
    {
        var minimize = this.FindControl<Button>("MinimizeButton");
        if (minimize is not null)
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var maximize = this.FindControl<Button>("MaximizeButton");
        if (maximize is not null)
        {
            _maximizeIcon = maximize.Content as Material.Icons.Avalonia.MaterialIcon;
            maximize.Click += (_, _) => ToggleMaximize();
        }

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
            close.Click += (_, _) => Exit_Click(this, new RoutedEventArgs());

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                UpdateMaximizeIcon();
        };

        var titleBar = this.FindControl<Border>("CustomTitleBar");
        if (titleBar is not null)
            WireTitleBarDrag(titleBar);
    }

    private void WireTitleBarDrag(Border titleBar)
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

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

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeIcon()
    {
        if (_maximizeIcon is null) return;
        _maximizeIcon.Kind = WindowState == WindowState.Maximized
            ? Material.Icons.MaterialIconKind.WindowRestore
            : Material.Icons.MaterialIconKind.WindowMaximize;
    }
}
