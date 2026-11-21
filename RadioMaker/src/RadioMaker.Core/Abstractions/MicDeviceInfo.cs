namespace RadioMaker.Core.Abstractions;

/// -------------
/// Info de un dispositivo de entrada (microfono) disponible en el sistema.
/// -------------
public sealed record MicDeviceInfo(
    int Index,
    string Name,
    string Driver,
    bool IsEnabled,
    bool IsDefault,
    bool IsInitialized)
{
    public string DisplayName =>
        Index < 0
            ? Name
            : $"[{Index}] {Name}{(IsDefault ? " (predeterminado)" : string.Empty)}";

    public override string ToString() => DisplayName;
}