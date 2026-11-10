namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Datos de eventos disparados al finalizar la reproducción de un canal.
/// </summary>
public sealed class ChannelEndedEventArgs : EventArgs
{
    /// <summary>
    /// <c>true</c> si el canal se detuvo por acción explícita del usuario (Stop),
    /// <c>false</c> si terminó de reproducir de forma natural.
    /// </summary>
    public bool WasStoppedByUser { get; init; }
}
