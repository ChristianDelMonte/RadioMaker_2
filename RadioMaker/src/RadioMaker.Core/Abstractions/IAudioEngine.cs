namespace RadioMaker.Core.Abstractions;

/// -------------
/// Fachada del motor de audio. Abstrae el backend (BASS) para que la UI
/// y los modulos de la aplicacion trabajen contra una interfaz estable.
///
/// Modelo de buses:
///   • El engine mantiene una coleccion de IAudioBus.
///   • Cada bus tiene su propio dispositivo de salida BASS y su propio volumen master.
///   • Los canales se cargan siempre a traves de un bus concreto.
///
/// Ducking:
///   • El engine es duenio de un unico DuckingProcessor.
///   • Cuando esta activo, el procesador captura el microfono, mide su nivel
///     y aplica una ganancia de atenuacion a TODOS los canales de TODOS los buses.
///   • La ganancia se actualiza suavemente a 50 Hz.
/// -------------
public interface IAudioEngine : IDisposable
{
    bool IsInitialized { get; }
    IReadOnlyList<IAudioBus> Buses { get; }
    IAudioBus PrimaryBus { get; }

    /// -------------
    /// Volumen maestro global de la aplicacion (0.0 .. 1.0). Se multiplica
    /// junto con el resto de la cadena de volumen de TODOS los canales de
    /// TODOS los buses. Un valor de 1.0 = sin atenuacion.
    /// -------------
    double GlobalVolume { get; set; }
    IReadOnlyList<FormatPluginInfo> LoadedPlugins { get; }
    IReadOnlyList<string> SupportedExtensions { get; }

    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();

    /// -------------Enumera los dispositivos de entrada (microfonos) disponibles.</summary>
    IReadOnlyList<MicDeviceInfo> EnumerateMicrophones();

    void Initialize(int deviceIndex = -1, int sampleRate = 44100);

    IAudioBus CreateBus(string displayName, int deviceIndex);
    bool RemoveBus(string busId);
    IAudioBus? GetBus(string busId);

    // ───── Ducking ─────────────────────────────────────────────────────────

    /// -------------Indica si el procesador de ducking esta corriendo.</summary>
    bool IsDuckingActive { get; }

    /// -------------Ganancia de ducking actual (dB, siempre &lt;= 0). -25 dB tipico.</summary>
    double CurrentDuckingGainDb { get; }

    /// -------------Nivel actual del microfono (dBFS). Util para mostrar un VU de entrada.</summary>
    double CurrentMicLevelDb { get; }

    /// -------------Se dispara cuando cambia cualquier parametro del ducking o su estado.</summary>
    event EventHandler? DuckingStateChanged;

    /// -------------
    /// Inicia o detiene el procesador de ducking con la configuracion dada.
    /// Es seguro llamarlo multiples veces — el engine aplica el cambio incremental.
    /// -------------
    void ApplyDuckingConfig(DuckingConfig config);

    /// -------------Detiene el procesador de ducking si esta corriendo.</summary>
    void StopDucking();

    void Shutdown();
}