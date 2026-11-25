using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Localization;

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
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AudioBuses.Status.Removed"), vm.DisplayName);
            BusesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StatusMessage = LocalizationService.GetString("Loc.AudioBuses.Status.CannotRemove");
        }
    }

    [RelayCommand]
    private void AddBus()
    {
        try
        {
            var newBus = _engine.CreateBus(LocalizationService.GetString("Loc.AudioBuses.NewBus"), deviceIndex: -1);
            var vm = new BusViewModel(newBus, isPrimary: false, _engine.EnumerateDevices);
            vm.RemoveRequested += OnRemoveRequested;
            Buses.Add(vm);
            SelectedBus = vm;
            StatusMessage = LocalizationService.GetString("Loc.AudioBuses.Status.Added");
            BusesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.AudioBuses.Status.CreateError"), ex.Message);
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        foreach (var bus in Buses)
            bus.RefreshDevices();

        StatusMessage = LocalizationService.GetString("Loc.AudioBuses.Status.DevicesUpdated");
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
