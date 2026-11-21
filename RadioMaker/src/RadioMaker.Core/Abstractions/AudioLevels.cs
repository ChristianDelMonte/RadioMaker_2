namespace RadioMaker.Core.Abstractions;

/// -------------
/// Lectura instantánea de niveles de un canal de audio.
/// Valores en amplitud lineal (0.0 = silencio, 1.0 = full scale).
/// -------------
public readonly record struct AudioLevels(
    double LeftPeak,
    double RightPeak,
    double LeftRms,
    double RightRms,
    bool LeftClip,
    bool RightClip)
{
    public static readonly AudioLevels Silent = new(0, 0, 0, 0, false, false);

    public double MaxPeak => Math.Max(LeftPeak, RightPeak);
    public bool AnyClip => LeftClip || RightClip;
}
