using System;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Item pendiente de disparar en el panel de Eventos.
/// Una linea por (item programado + entrada de horario activa).
/// -------------
public sealed class PendingScheduledItem
{
    public DateTime NextFireTime { get; init; }
    public string ListId { get; init; } = "";
    public string ListName { get; init; } = "";
    public string ItemId { get; init; } = "";
    public string ItemTitle { get; init; } = "";
    public string UnderlyingFile { get; init; } = "";
    public ScheduledItemKind Kind { get; init; }
    public SchedulePriority Priority { get; init; }
    public double DurationSeconds { get; init; }

    /// -------------Indice 1-based dentro del dia.</summary>
    public int DayIndex { get; set; }

    public string TimeText => NextFireTime.ToString("HH:mm:ss");

    public string EndTimeText
    {
        get
        {
            if (DurationSeconds <= 0.5) return "";
            var end = NextFireTime.AddSeconds(DurationSeconds);
            var dayOffset = (end.Date - NextFireTime.Date).Days;
            return dayOffset > 0
                ? $"{end:HH:mm:ss} (+{dayOffset}d)"
                : end.ToString("HH:mm:ss");
        }
    }

    public string KindText => Kind switch
    {
        ScheduledItemKind.Tanda => LocalizationService.GetString("Loc.ScheduledItem.Kind.Sequence"),
        ScheduledItemKind.Playlist => LocalizationService.GetString("Loc.ScheduledItem.Kind.Playlist"),
        ScheduledItemKind.Block => LocalizationService.GetString("Loc.ScheduledItem.Kind.Block"),
        _ => LocalizationService.GetString("Loc.ScheduledItem.Kind.File")
    };

    public string KindColor => Kind switch
    {
        ScheduledItemKind.Tanda => "Accent",
        ScheduledItemKind.Playlist => "Success",
        ScheduledItemKind.Block => "Warning",
        _ => "FgMuted"
    };

    public string PriorityText => Priority == SchedulePriority.High ? "ALTA" : "BAJA";
    public string PriorityColor => Priority == SchedulePriority.High ? "Warning" : "FgMuted";

    public string DurationText
    {
        get
        {
            if (DurationSeconds <= 0.5) return "";
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }
    }

    public bool HasFile => !string.IsNullOrWhiteSpace(UnderlyingFile);
    public bool HasDuration => DurationSeconds > 0.5;
    public bool HasEndTime => HasDuration;
}
