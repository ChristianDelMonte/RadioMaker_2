using RMVoice.Core.Models;

namespace RMVoice.Core;

/// -------------
/// Fachada pública del motor de locución.
/// -------------
public interface IAnnouncer : IDisposable
{
    // ----------------------------- Estado -----------------------------

    bool IsInitialized { get; }
    bool IsPlaying { get; }
    /// -------------Handle BASS del segmento de locución que suena actualmente, o cero.
    int CurrentStreamHandle { get; }

    int DefaultDevice { get; set; }
    float Volume { get; set; }

    // --------------------------- Perfiles -----------------------------

    IReadOnlyList<VoiceProfile> Profiles { get; }
    VoiceProfile? ActiveProfile { get; set; }

    // ------------------------- Ciclo de vida --------------------------

    void Initialize();

    /// -------------Vuelve a leer <c>profiles.json</c> desde disco.
    void ReloadProfiles();

    /// -------------
    /// Recarga el perfil activo desde disco sin cambiar de perfil.
    /// Útil cuando el usuario editó las tablas de regiones y quiere que el
    /// Engine las use sin reiniciar.
    /// -------------
    void ReloadActiveProfile();

    // -------------------------- Reproducción --------------------------

    Task PlayAsync(AnnounceRequest request, CancellationToken ct = default);
    void Play(AnnounceRequest request);
    void Stop();

    // ---------------------------- Clima -------------------------------

    Task<List<WeatherLocation>> SearchLocationsAsync(string query, CancellationToken ct = default);
    Task<WeatherSnapshot> FetchWeatherAsync(CancellationToken ct = default);
    WeatherSnapshot? LastWeather { get; }
    WeatherLocation? ActiveWeatherLocation { get; set; }

    // ---------------------------- Eventos -----------------------------

    event EventHandler<AnnounceRequest>? PlaybackStarted;
    event EventHandler<AnnounceRequest>? PlaybackCompleted;
    event EventHandler<Exception>? PlaybackFailed;
}
