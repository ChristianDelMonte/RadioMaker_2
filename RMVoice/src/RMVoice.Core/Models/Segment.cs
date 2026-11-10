namespace RMVoice.Core.Models;

/// <summary>
/// Un elemento de la cola de reproducción: una pista concreta y una región concreta.
/// </summary>
public readonly record struct Segment(TrackKind Kind, AudioRegion Region);
