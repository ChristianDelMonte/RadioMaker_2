namespace RMVoice.Core.Models;

/// -------------
/// Un elemento de la cola de reproducción: una pista concreta y una región concreta.
/// -------------
public readonly record struct Segment(TrackKind Kind, AudioRegion Region);
