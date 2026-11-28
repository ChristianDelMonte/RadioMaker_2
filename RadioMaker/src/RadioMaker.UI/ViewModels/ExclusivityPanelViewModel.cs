using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Configuration;
using RadioMaker.Core.Logging;
using RadioMaker.Core.Playback;

namespace RadioMaker.UI.ViewModels;

public sealed partial class ExclusivityPanelViewModel : ViewModelBase
{
    private readonly BusExclusivityCoordinator? _coordinator;

    public ObservableCollection<ModulePolicyItemViewModel> ModulePolicies { get; } = new();

    public ExclusivityPanelViewModel(BusExclusivityCoordinator? coordinator)
    {
        _coordinator = coordinator;
        LoadPolicies();
    }

    private void LoadPolicies()
    {
        ModulePolicies.Clear();
        if (_coordinator is null) return;

        foreach (var role in Enum.GetValues<ModuleRole>())
        {
            if (role == ModuleRole.Manual) continue;

            var policy = _coordinator.Config.GetPolicy(role);
            ModulePolicies.Add(new ModulePolicyItemViewModel(role, policy, SavePolicies));
        }
    }

    private void SavePolicies()
    {
        if (_coordinator is null) return;

        try
        {
            BusExclusivityConfigStore.Save(_coordinator.Config);
        }
        catch (Exception ex)
        {
            AppLogger.Error("SETTINGS EXCLUSIVITY", "No se pudieron guardar las políticas de exclusividad", ex);
        }
    }

    [RelayCommand]
    private void ResetPoliciesToDefaults()
    {
        if (_coordinator is null) return;

        _coordinator.ResetPoliciesToDefaults();
        LoadPolicies();
        SavePolicies();
        AppLogger.Info("SETTINGS EXCLUSIVITY", "Políticas de exclusividad restablecidas a sus valores predeterminados.");
    }
}
