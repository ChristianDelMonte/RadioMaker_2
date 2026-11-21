using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel de la ventana de configuración de buses.
/// Permite ver, agregar, eliminar y editar los buses de audio de la aplicación.
/// -------------
public sealed partial class AudioBusesWindowViewModel : ViewModelBase
{
    private readonly IAudioEngine _engine;

    [ObservableProperty] private BusViewModel? _selectedBus;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<BusViewModel> Buses { get; } = new();

    /// -------------Se dispara cuando el usuario confirma/cierra con cambios aplicables.</summary>
    public event EventHandler? BusesChanged;

    public AudioBusesWindowViewModel(IAudioEngine engine)
    {
        _engine = engine;
        Reload();
    }

    private void Reload()
    {
        Buses.Clear();

        var list = _engine.Buses;
        for (int i = 0; i < list.Count; i++)
        {
            var bus = list[i];
            var vm = new BusViewModel(bus, bus.IsPrimary, _engine.EnumerateDevices);
            vm.RemoveRequested += OnRemoveRequested;
            Buses.Add(vm);
        }

        SelectedBus = Buses.FirstOrDefault();
    }

    private void OnRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is not BusViewModel vm) return;
        if (!vm.CanRemove) return;

        var removed = _engine.RemoveBus(vm.Id);
        if (removed)
        {
            vm.RemoveRequested -= OnRemoveRequested;
            Buses.Remove(vm);
            SelectedBus = Buses.FirstOrDefault();
            StatusMessage = $"Bus '{vm.DisplayName}' eliminado.";
            BusesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StatusMessage = "No se puede eliminar ese bus.";
        }
    }

    [RelayCommand]
    private void AddBus()
    {
        try
        {
            var newBus = _engine.CreateBus("Nuevo Bus", deviceIndex: -1);
            var vm = new BusViewModel(newBus, isPrimary: false, _engine.EnumerateDevices);
            vm.RemoveRequested += OnRemoveRequested;
            Buses.Add(vm);
            SelectedBus = vm;
            StatusMessage = "Bus agregado. Asignale un nombre y un dispositivo.";
            BusesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al crear bus: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        foreach (var bus in Buses)
            bus.RefreshDevices();

        StatusMessage = "Lista de dispositivos actualizada.";
    }

    /// -------------Genera la lista de configuración serializable a partir del estado actual.</summary>
    public IReadOnlyList<AudioBusConfig> ToConfigs()
    {
        var list = new List<AudioBusConfig>();
        for (int i = 0; i < Buses.Count; i++)
        {
            var vm = Buses[i];
            list.Add(new AudioBusConfig
            {
                Id = vm.Id,
                DisplayName = vm.DisplayName,
                DeviceIndex = vm.SelectedDevice?.Index ?? -1,
                MasterVolume = vm.MasterVolume,
                IsPrimary = vm.IsPrimary
            });
        }
        return list;
    }
}
