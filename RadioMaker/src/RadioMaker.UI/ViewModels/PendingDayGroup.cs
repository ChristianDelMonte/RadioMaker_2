using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Grupo de items pendientes agrupados por dia.
/// -------------
public sealed class PendingDayGroup
{
    public DateTime Date { get; init; }
    public string Header { get; init; } = "";
    public ObservableCollection<PendingScheduledItem> Items { get; } = new();
}