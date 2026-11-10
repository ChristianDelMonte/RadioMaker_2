using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Wrapper de UI sobre <see cref="ScheduleEntry"/>.
/// Un wrapper por cada entrada de horario dentro de un item programado.
/// </summary>
public sealed partial class ScheduleEntryViewModel : ViewModelBase
{
    private readonly ScheduleEntry _entry;

    public ScheduleEntry Source => _entry;

    /// <summary>Se dispara cuando cambia cualquier propiedad relevante.</summary>
    public event EventHandler? Changed;

    [ObservableProperty] private int _hour;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private bool _isEnabled;

    // ───── Dias de la semana ─────────────────────────────────────────────

    [ObservableProperty] private bool _monday;
    [ObservableProperty] private bool _tuesday;
    [ObservableProperty] private bool _wednesday;
    [ObservableProperty] private bool _thursday;
    [ObservableProperty] private bool _friday;
    [ObservableProperty] private bool _saturday;
    [ObservableProperty] private bool _sunday;

    // ───── Comandos rapidos de dias ──────────────────────────────────────

    public string ShortDescription => BuildShortDescription();

    // ───── Constructor ───────────────────────────────────────────────────

    public ScheduleEntryViewModel(ScheduleEntry entry)
    {
        _entry = entry;

        _hour = entry.Hour;
        _minute = entry.Minute;
        _isEnabled = entry.IsEnabled;

        _monday = entry.Days.Contains(DayOfWeek.Monday);
        _tuesday = entry.Days.Contains(DayOfWeek.Tuesday);
        _wednesday = entry.Days.Contains(DayOfWeek.Wednesday);
        _thursday = entry.Days.Contains(DayOfWeek.Thursday);
        _friday = entry.Days.Contains(DayOfWeek.Friday);
        _saturday = entry.Days.Contains(DayOfWeek.Saturday);
        _sunday = entry.Days.Contains(DayOfWeek.Sunday);
    }

    /// <summary>Copia el estado del VM al modelo subyacente.</summary>
    public void ApplyToModel()
    {
        _entry.Hour = Math.Clamp(Hour, 0, 23);
        _entry.Minute = Math.Clamp(Minute, 0, 59);
        _entry.IsEnabled = IsEnabled;

        _entry.Days.Clear();
        if (Monday) _entry.Days.Add(DayOfWeek.Monday);
        if (Tuesday) _entry.Days.Add(DayOfWeek.Tuesday);
        if (Wednesday) _entry.Days.Add(DayOfWeek.Wednesday);
        if (Thursday) _entry.Days.Add(DayOfWeek.Thursday);
        if (Friday) _entry.Days.Add(DayOfWeek.Friday);
        if (Saturday) _entry.Days.Add(DayOfWeek.Saturday);
        if (Sunday) _entry.Days.Add(DayOfWeek.Sunday);
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnHourChanged(int value) { ApplyToModel(); NotifyChanged(); }
    partial void OnMinuteChanged(int value) { ApplyToModel(); NotifyChanged(); }
    partial void OnIsEnabledChanged(bool value) { ApplyToModel(); NotifyChanged(); }

    partial void OnMondayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnTuesdayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnWednesdayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnThursdayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnFridayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnSaturdayChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnSundayChanged(bool value) { ApplyToModel(); NotifyChanged(); }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(ShortDescription));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string BuildShortDescription()
    {
        var dayList = new List<string>();
        if (Monday) dayList.Add("Lun");
        if (Tuesday) dayList.Add("Mar");
        if (Wednesday) dayList.Add("Mie");
        if (Thursday) dayList.Add("Jue");
        if (Friday) dayList.Add("Vie");
        if (Saturday) dayList.Add("Sab");
        if (Sunday) dayList.Add("Dom");

        string days = dayList.Count switch
        {
            0 => "—",
            7 => "Todos los dias",
            _ => string.Join("/", dayList)
        };

        return $"{days} {Hour:D2}:{Minute:D2}";
    }
}