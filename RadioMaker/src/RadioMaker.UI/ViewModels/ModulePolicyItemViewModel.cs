using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Fila editable de la tabla de politicas de exclusividad para un modulo.
/// Ahora expone tambien <see cref="Behavior"/>.
///
/// Recordar el modelo: <see cref="Behavior"/> describe COMO TRATA este
/// modulo al expropiado cuando lo saca del bus (no como quiere ser tratado).
/// -------------
public sealed partial class ModulePolicyItemViewModel : ViewModelBase
{
    private readonly ModuleExclusivityPolicy _policy;
    private readonly Action? _onChanged;

    public ModuleRole Role { get; }
    public string RoleLabel { get; }

    [ObservableProperty] private bool _isExclusive;
    [ObservableProperty] private ModulePriority _priority;
    [ObservableProperty] private InterruptionBehavior _behavior;

    public ModulePriority[] AvailablePriorities { get; } =
    {
        ModulePriority.Low,
        ModulePriority.Normal,
        ModulePriority.High,
        ModulePriority.Air
    };

    public InterruptionBehavior[] AvailableBehaviors { get; } =
    {
        InterruptionBehavior.Stop,
        InterruptionBehavior.Pause,
        InterruptionBehavior.AutoResume
    };

    public ModulePolicyItemViewModel(
        ModuleRole role,
        ModuleExclusivityPolicy policy,
        Action? onChanged = null)
    {
        Role = role;
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _onChanged = onChanged;

        RoleLabel = GetRoleLabel(role);

        _isExclusive = policy.IsExclusive;
        _priority = policy.Priority;
        _behavior = policy.Behavior;
    }

    partial void OnIsExclusiveChanged(bool value)
    {
        _policy.IsExclusive = value;
        AppLogger.Info("SETTINGS EXCLUSIVITY", $"{Role}: Exclusive={value}; Priority={Priority}; Behavior={Behavior}.");
        try { _onChanged?.Invoke(); } catch { }
    }

    partial void OnPriorityChanged(ModulePriority value)
    {
        _policy.Priority = value;
        AppLogger.Info("SETTINGS EXCLUSIVITY", $"{Role}: Exclusive={IsExclusive}; Priority={value}; Behavior={Behavior}.");
        try { _onChanged?.Invoke(); } catch { }
    }

    partial void OnBehaviorChanged(InterruptionBehavior value)
    {
        _policy.Behavior = value;
        AppLogger.Info("SETTINGS EXCLUSIVITY", $"{Role}: Exclusive={IsExclusive}; Priority={Priority}; Behavior={value}.");
        try { _onChanged?.Invoke(); } catch { }
    }

    private static string GetRoleLabel(ModuleRole role) => role switch
    {
        ModuleRole.Cart => "Botonera",
        ModuleRole.Playlist => "Playlists",
        ModuleRole.Scheduler => "Programados",
        ModuleRole.FileManager => "Explorador de archivos",
        ModuleRole.FavoriteFolder => "Carpetas favoritas",
        ModuleRole.Manual => "Reproduccion manual",
        _ => role.ToString()
    };
}
