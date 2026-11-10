using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel compartido por todos los selectores de bus por modulo.
///
/// Cada modulo (Cart, Playlist, Scheduler, FileManager) tiene UNA instancia.
/// Expone la lista de buses disponibles y el bus actual del modulo.
///
/// Cuando el servicio tiene UseGlobalBus = true, CanChangeBus = false y el
/// selector queda deshabilitado mostrando el bus global.
/// </summary>
public sealed partial class ModuleBusSelectorViewModel : ViewModelBase
{
    private readonly ModuleRoutingService _service;
    private readonly AudioModuleKind _kind;

    private bool _suppressApply;

    public ObservableCollection<IAudioBus> AvailableBuses { get; } = new();

    [ObservableProperty] private IAudioBus? _selectedBus;
    [ObservableProperty] private bool _canChangeBus;

    public AudioModuleKind Kind => _kind;

    public ModuleBusSelectorViewModel(ModuleRoutingService service, AudioModuleKind kind)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _kind = kind;

        RefreshBuses();
        SyncFromService();

        _service.Changed += OnServiceChanged;
        _service.BusesListChanged += OnBusesListChanged;
    }

    // ───── Reaccion a cambios del servicio ──────────────────────────────

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        SyncFromService();
    }

    private void OnBusesListChanged(object? sender, EventArgs e)
    {
        RefreshBuses();
        SyncFromService();
    }

    // ───── Sincronizacion ───────────────────────────────────────────────

    private void SyncFromService()
    {
        _suppressApply = true;
        try
        {
            CanChangeBus = !_service.UseGlobalBus;

            var currentId = _service.GetConfiguredBusId(_kind);
            var target = AvailableBuses.FirstOrDefault(b => b.Id == currentId)
                         ?? _service.GetEffectiveBus(_kind);

            if (!ReferenceEquals(SelectedBus, target))
                SelectedBus = target;
        }
        finally
        {
            _suppressApply = false;
        }
    }

    public void RefreshBuses()
    {
        var currentId = SelectedBus?.Id;

        AvailableBuses.Clear();
        foreach (var b in _service.AvailableBuses)
            AvailableBuses.Add(b);

        if (!string.IsNullOrEmpty(currentId))
        {
            var target = AvailableBuses.FirstOrDefault(b => b.Id == currentId);
            if (target is not null)
            {
                _suppressApply = true;
                try { SelectedBus = target; }
                finally { _suppressApply = false; }
            }
        }
    }

    // ───── Aplicacion al servicio ───────────────────────────────────────

    partial void OnSelectedBusChanged(IAudioBus? value)
    {
        if (_suppressApply) return;
        if (value is null) return;
        if (!CanChangeBus) return;

        _service.SetModuleBus(_kind, value);
    }
}