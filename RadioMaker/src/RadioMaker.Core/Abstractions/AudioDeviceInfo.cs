namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Información de un dispositivo de audio de salida disponible en el sistema.
/// </summary>
public sealed record AudioDeviceInfo(
    int Index,
    string Name,
    string Driver,
    bool IsEnabled,
    bool IsDefault,
    bool IsInitialized)
{
    /// <summary>Representación legible para mostrar en un ComboBox.</summary>
    public string DisplayName =>
        Index < 0
            ? Name
            : $"[{Index}] {Name}{(IsDefault ? " (predeterminado)" : string.Empty)}";

    public override string ToString() => DisplayName;
}
