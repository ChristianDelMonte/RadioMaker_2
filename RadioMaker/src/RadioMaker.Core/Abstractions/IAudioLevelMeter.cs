namespace RadioMaker.Core.Abstractions;

/// -------------
/// Fuente de lecturas de nivel para un canal o bus.
/// El llamador decide cada cuánto consultar (típicamente 20–60 Hz).
/// -------------
public interface IAudioLevelMeter
{
    AudioLevels Read();
}
