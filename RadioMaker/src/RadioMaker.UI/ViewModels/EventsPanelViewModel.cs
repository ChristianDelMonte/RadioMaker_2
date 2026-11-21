using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Panel de Eventos: muestra los items programados pendientes de disparar
/// (agrupados por dia) y un log compacto abajo.
///
/// Los items pendientes se calculan a partir de las ScheduledList ACTIVAS
/// (el switch de cada pestaña de Programados controla si participa o no).
///
/// Se refresca cada 2 segundos o cuando cambia el registry.
/// -------------
public sealed partial class EventsPanelViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    public SchedulerPanelViewModel? Scheduler { get; }

    /// -------------Grupos de items pendientes por dia.</summary>
    public ObservableCollection<PendingDayGroup> PendingGroups { get; } = new();

    /// -------------Log del scheduler (bindea directo al del SchedulerPanelViewModel).</summary>
    public ObservableCollection<SchedulerLogEntry> LogEntries
        => Scheduler?.LogEntries ?? EmptyLog;

    private static readonly ObservableCollection<SchedulerLogEntry> EmptyLog = new();

    // ───── Estado observable ─────────────────────────────────────────────

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _activeListCount;
    [ObservableProperty] private int _disabledListCount;

    public bool HasPending => PendingCount > 0;
    public bool HasNoPending => !HasPending;
    public bool IsSchedulerRunning => Scheduler?.IsRunning ?? false;

    // ───── Propiedades de conveniencia para el panel "En Reproducción" ───
    // Delegamos al Scheduler (que ya tiene los bindings a PlaybackEngine y
    // PlaylistPlayer legacy). Re-emitimos los cambios desde OnSchedulerPropertyChanged.
    // Nombres simplificados para que el XAML quede legible.

    public PlaybackStateInfo PlaybackState => Scheduler?.PlaybackState ?? PlaybackStateInfo.Idle;
    public PlaylistPlayerState PlaylistState => Scheduler?.PlaylistState ?? PlaylistPlayerState.Stopped;

    public bool IsSchedulerActive => Scheduler?.IsSchedulerActive ?? false;
    public bool IsPlaylistActive => Scheduler?.IsPlaylistActive ?? false;
    public bool IsPlaylistSuspended => Scheduler?.IsPlaylistSuspended ?? false;
    public bool ShowPlaylistPanel => Scheduler?.ShowPlaylistPanel ?? false;
    public bool ShowEmptyState => Scheduler?.ShowEmptyState ?? true;
    public bool ShowSchedulerPanel => IsSchedulerActive;

    public string PlaylistHeaderText => Scheduler?.PlaylistHeaderText ?? "";
    public string PlaylistTrackText => Scheduler?.PlaylistTrackText ?? "";
    public string PlaylistProgressText => Scheduler?.PlaylistProgressText ?? "";
    public string PlaylistPositionText => Scheduler?.PlaylistPositionText ?? "";
    public double PlaylistProgressFraction => Scheduler?.PlaylistProgressFraction ?? 0;
    public string PlaylistStatusText => Scheduler?.PlaylistStatusText ?? "";
    public string PlaylistStatusColorKey => Scheduler?.PlaylistStatusColorKey ?? "FgMuted";

    // ───── Comandos de la toolbar ────────────────────────────────────────

    [RelayCommand]
    private void ToggleScheduler()
    {
        Scheduler?.ToggleRunCommand.Execute(null);
        OnPropertyChanged(nameof(IsSchedulerRunning));
    }

    [RelayCommand]
    private void ResetState()
    {
        Scheduler?.ResetStateCommand.Execute(null);
        RefreshPending();
    }

    [RelayCommand]
    private void ClearLog() => Scheduler?.ClearLogCommand.Execute(null);

    // ───── Constructor ───────────────────────────────────────────────────

    public EventsPanelViewModel(SchedulerPanelViewModel? scheduler = null)
    {
        Scheduler = scheduler;

        if (Scheduler?.Registry is { } reg)
        {
            reg.Changed += OnRegistryChanged;
        }

        // ===== Notificar cambios de estado del scheduler (IsRunning) =====
        // Sin esto, el boton Play/Pause y el indicador ACTIVO/DETENIDO de
        // la toolbar no se actualizaban visualmente al iniciar/detener el
        // scheduler.
        if (Scheduler is not null)
        {
            Scheduler.PropertyChanged += OnSchedulerPropertyChanged;
        }

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => RefreshPending();
        _refreshTimer.Start();

        RefreshPending();
    }

    private void OnSchedulerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchedulerPanelViewModel.IsRunning))
        {
            OnPropertyChanged(nameof(IsSchedulerRunning));
            return;
        }

        // Cualquier otro cambio del Scheduler que afecte al panel
        // "En Reproducción" → re-emitir todas las propiedades derivadas.
        // Es barato (los cambios de PlaybackState/PlaylistState no son
        // frecuentes) y evita bugs de bindings que no se actualizan.
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(PlaylistState));
        OnPropertyChanged(nameof(IsSchedulerActive));
        OnPropertyChanged(nameof(IsPlaylistActive));
        OnPropertyChanged(nameof(IsPlaylistSuspended));
        OnPropertyChanged(nameof(ShowPlaylistPanel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSchedulerPanel));
        OnPropertyChanged(nameof(PlaylistHeaderText));
        OnPropertyChanged(nameof(PlaylistTrackText));
        OnPropertyChanged(nameof(PlaylistProgressText));
        OnPropertyChanged(nameof(PlaylistPositionText));
        OnPropertyChanged(nameof(PlaylistProgressFraction));
        OnPropertyChanged(nameof(PlaylistStatusText));
        OnPropertyChanged(nameof(PlaylistStatusColorKey));
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshPending);
    }

    // ───── Refresh ───────────────────────────────────────────────────────

    /// -------------
    /// Recalcula los items pendientes a partir del registry. Incluye solo
    /// ScheduledLists habilitadas + items habilitados + entries habilitadas.
    /// -------------
    private void RefreshPending()
    {
        if (_disposed) return;

        try
        {
            var registry = Scheduler?.Registry;
            if (registry is null)
            {
                PendingGroups.Clear();
                PendingCount = 0;
                ActiveListCount = 0;
                DisabledListCount = 0;
                StatusText = "Sin scheduler.";
                return;
            }

            var allLists = registry.GetAll();
            var enabledLists = registry.GetEnabled();

            ActiveListCount = enabledLists.Count;
            DisabledListCount = allLists.Count - enabledLists.Count;

            var now = DateTime.Now;
            var pending = new List<PendingScheduledItem>();

            foreach (var list in enabledLists)
            {
                if (list.Items is null) continue;

                foreach (var item in list.Items)
                {
                    if (item is null) continue;
                    if (!item.IsEnabled) continue;
                    if (item.Entries is null || item.Entries.Count == 0) continue;

                    foreach (var entry in item.Entries)
                    {
                        if (entry is null) continue;
                        if (!entry.IsEnabled) continue;

                        var next = ComputeNextOccurrence(entry, now);
                        if (next is null) continue;

                        pending.Add(new PendingScheduledItem
                        {
                            NextFireTime = next.Value,
                            ListId = list.Id,
                            ListName = list.DisplayName,
                            ItemId = item.Id,
                            ItemTitle = item.DisplayTitle,
                            UnderlyingFile = ResolveUnderlyingFile(item),
                            Kind = item.Kind,
                            Priority = item.Priority,
                            DurationSeconds = item.DurationSeconds
                        });
                    }
                }
            }

            pending.Sort((a, b) => a.NextFireTime.CompareTo(b.NextFireTime));

            // Agrupar por dia.
            PendingGroups.Clear();

            foreach (var group in pending.GroupBy(p => p.NextFireTime.Date))
            {
                var dayGroup = new PendingDayGroup
                {
                    Date = group.Key,
                    Header = BuildHeader(group.Key, now)
                };

                int idx = 1;
                foreach (var p in group)
                {
                    p.DayIndex = idx++;
                    dayGroup.Items.Add(p);
                }

                PendingGroups.Add(dayGroup);
            }

            PendingCount = pending.Count;

            StatusText = pending.Count == 0
                ? "Sin items pendientes."
                : $"{pending.Count} item(s) pendiente(s) en {enabledLists.Count} lista(s) activa(s)." +
                  (DisabledListCount > 0 ? $" {DisabledListCount} lista(s) desactivada(s)." : "");

            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasNoPending));
        }
        catch
        {
            // Silencioso.
        }
    }

    // ───── Helpers de calculo ────────────────────────────────────────────

    /// -------------
    /// Devuelve la proxima ocurrencia de una entrada de horario a partir de
    /// <paramref name="now"/>. Null si la entrada no tiene dias.
    /// -------------
    private static DateTime? ComputeNextOccurrence(ScheduleEntry entry, DateTime now)
    {
        if (entry.Days is null || entry.Days.Count == 0) return null;

        for (int offset = 0; offset <= 7; offset++)
        {
            var candidateDate = now.Date.AddDays(offset);
            if (!entry.Days.Contains(candidateDate.DayOfWeek)) continue;

            var candidate = candidateDate
                .AddHours(entry.Hour)
                .AddMinutes(entry.Minute);

            if (candidate > now) return candidate;
        }

        return null;
    }

    private static string ResolveUnderlyingFile(ScheduledItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath)) return "";
        try { return System.IO.Path.GetFileName(item.FilePath); }
        catch { return item.FilePath; }
    }

    private static string BuildHeader(DateTime date, DateTime now)
    {
        var dayName = date.ToString("dddd", new CultureInfo("es-ES"));
        var dateText = date.ToString("d 'de' MMMM", new CultureInfo("es-ES"));

        // Capitalizar primera letra.
        if (!string.IsNullOrEmpty(dayName))
            dayName = char.ToUpper(dayName[0]) + dayName.Substring(1);

        var diff = (date.Date - now.Date).Days;

        return diff switch
        {
            0 => $"Pendientes de Hoy ({dayName}, {dateText})",
            1 => $"Pendientes de Mañana ({dayName}, {dateText})",
            _ => $"Pendientes del {dayName} {dateText}"
        };
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _refreshTimer.Stop(); } catch { }

        if (Scheduler?.Registry is { } reg)
        {
            try { reg.Changed -= OnRegistryChanged; } catch { }
        }

        if (Scheduler is not null)
        {
            try { Scheduler.PropertyChanged -= OnSchedulerPropertyChanged; } catch { }
        }
    }
}