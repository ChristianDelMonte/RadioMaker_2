namespace RadioMaker.Core.Abstractions;

/// -------------
/// Datos de eventos disparados al finalizar la reproducción de un canal.
/// -------------
public sealed class ChannelEndedEventArgs : EventArgs
{
    /// -------------
    /// <c>true</c> si el canal se detuvo por acción explícita del usuario (Stop),
    /// <c>false</c> si terminó de reproducir de forma natural.
    /// -------------
    public bool WasStoppedByUser { get; init; }
}
