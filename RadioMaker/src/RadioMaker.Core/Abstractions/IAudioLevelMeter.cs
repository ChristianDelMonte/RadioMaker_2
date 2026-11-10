namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Fuente de lecturas de nivel para un canal o bus.
/// El llamador decide cada cuánto consultar (típicamente 20–60 Hz).
/// </summary>
public interface IAudioLevelMeter
{
    AudioLevels Read();
}
