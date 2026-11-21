namespace RadioMaker.UI.ViewModels;

/// -------------
/// Modo de mixado de una pista (para el editor de pista).
///
/// Mapea al modelo del AudioCore:
///   - None      -> MixContentType = Audio, MixAudioPath = ""
///   - Audio     -> MixContentType = Audio, MixAudioPath = "ruta"
///   - Locution  -> MixContentType = Locution
/// -------------
public enum TrackMixMode
{
    None = 0,
    Audio = 1,
    Locution = 2
}