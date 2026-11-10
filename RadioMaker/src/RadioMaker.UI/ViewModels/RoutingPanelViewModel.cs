using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;

namespace RadioMaker.UI.ViewModels;

public sealed partial class RoutingPanelViewModel : ViewModelBase
{
    public MainWindowViewModel? Main { get; }

    public ObservableCollection<IAudioBus> AvailableBuses { get; } = new();
    public ObservableCollection<ModulePolicyItemViewModel> ModulePolicies { get; } = new();

    [ObservableProperty] private IAudioBus? _selectedAirBus;

    private bool _suppressAirBusCallback;
    private bool _initialized;

    public RoutingPanelViewModel(MainWindowViewModel? main)
    {
        Main = main;

        RefreshBuses();
        LoadPolicies();
        SyncSelectedAirBus();

        if (main is not null)
        {
            main.Buses.CollectionChanged += (_, _) => RefreshBuses();
            main.Routing.Changed += (_, _) => SyncSelectedAirBus();
        }

        _initialized = true;
    }

    private void RefreshBuses()
    {
        AvailableBuses.Clear();
        if (Main is null) return;

        foreach (var b in Main.Buses)
            AvailableBuses.Add(b);
    }

    private void LoadPolicies()
    {
        ModulePolicies.Clear();
        if (Main?.Coordinator is not { } coordinator) return;

        foreach (ModuleRole role in Enum.GetValues(typeof(ModuleRole)))
        {
            // Manual no participa del sistema de exclusividad.
            if (role == ModuleRole.Manual) continue;

            var policy = coordinator.Config.GetPolicy(role);
            ModulePolicies.Add(new ModulePolicyItemViewModel(role, policy, SavePolicies));
        }
    }

    private void SavePolicies()
    {
        try
        {
            if (Main?.Coordinator is { } c)
                BusExclusivityConfigStore.Save(c.Config);
        }
        catch { }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ResetPoliciesToDefaults()
    {
        if (Main?.Coordinator is not { } coordinator) return;

        coordinator.ResetPoliciesToDefaults();
        LoadPolicies();
        SavePolicies();
    }

    private void SyncSelectedAirBus()
    {
        if (Main is null) return;

        _suppressAirBusCallback = true;
        try
        {
            var airBusId = Main.Routing.AirBusId;
            if (!string.IsNullOrEmpty(airBusId))
            {
                foreach (var bus in AvailableBuses)
                {
                    if (string.Equals(bus.Id, airBusId, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedAirBus = bus;
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressAirBusCallback = false;
        }
    }

    partial void OnSelectedAirBusChanged(IAudioBus? value)
    {
        if (!_initialized) return;
        if (_suppressAirBusCallback) return;
        if (value is null) return;
        if (Main is null) return;

        Main.Routing.AirBusId = value.Id;
    }
}