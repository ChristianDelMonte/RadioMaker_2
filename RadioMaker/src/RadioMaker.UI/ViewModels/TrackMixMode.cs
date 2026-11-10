namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Modo de mixado de una pista (para el editor de pista).
///
/// Mapea al modelo del AudioCore:
///   - None      -> MixContentType = Audio, MixAudioPath = ""
///   - Audio     -> MixContentType = Audio, MixAudioPath = "ruta"
///   - Locution  -> MixContentType = Locution
/// </summary>
public enum TrackMixMode
{
    None = 0,
    Audio = 1,
    Locution = 2
}