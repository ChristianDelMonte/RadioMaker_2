namespace RadioMaker.Core.Abstractions;

/// <summary>
/// Un Bus de audio representa una salida física independiente con su propio
/// dispositivo BASS, su propio volumen master y su propio conjunto de canales.
///
/// Ejemplos típicos de uso futuro:
///   • Bus "Aire"   → salida al transmisor/altavoces principales.
///   • Bus "Monitor"→ auriculares del operador.
///   • Bus "Preview"→ altavoces de estudio para preescucha.
/// </summary>
public interface IAudioBus : IDisposable
{
    /// <summary>Identificador inmutable del bus.</summary>
    string Id { get; }

    /// <summary>Nombre visible, editable en tiempo de ejecucion.</summary>
    string DisplayName { get; set; }

    /// <summary>Indice del dispositivo BASS asociado (‑1 = predeterminado).</summary>
    int DeviceIndex { get; }

    /// <summary>
    /// Indice REAL del dispositivo tras resolver -1 al default del sistema.
    /// Es el que usa BASS para operaciones como ChannelSetDevice.
    /// Se usa para que RMVoice (locuciones) reproduzca en el MISMO device
    /// que el audio del bus.
    /// </summary>
    int ResolvedDeviceIndex { get; }

    /// <summary>
    /// Nombre legible del dispositivo fisico asociado al bus.
    /// Ej: "Altavoces (Realtek High Definition Audio)".
    /// Si el bus usa el dispositivo predeterminado, describe eso.
    /// Se usa para logging y para mostrar en la UI.
    /// </summary>
    string DeviceDisplayName { get; }

    /// <summary>Volumen master del bus (0.0 .. 1.0). Aplica a todos los canales del bus.</summary>
    double MasterVolume { get; set; }

    /// <summary>Indica si el bus fue inicializado correctamente.</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Cantidad de canales ACTIVOS (State == Playing) en este bus.
    /// Util para el watchdog de emergencia que detecta "bus vacio".
    /// </summary>
    int ActiveChannelCount { get; }

    /// <summary>
    /// Niveles actuales agregados del bus (max peak entre todos los canales
    /// activos). Se usa para el watchdog de emergencia que detecta silencio
    /// digital aunque el canal exista.
    /// </summary>
    AudioLevels GetCurrentLevels();

    /// <summary>
    /// Inicializa el bus contra su dispositivo de salida. Es llamado
    /// automáticamente por <see cref="IAudioEngine.CreateBus"/> y no suele
    /// invocarse directamente.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Cambia el dispositivo de salida del bus. Libera los canales activos
    /// (BASS requiere Free + Init y eso invalida los handles existentes).
    /// </summary>
    bool ChangeDevice(int deviceIndex);

    /// <summary>
    /// Carga un archivo de audio en este bus y devuelve un canal reproducible.
    /// </summary>
    IAudioChannel LoadFile(string filePath);

    /// <summary>
    /// Reproduce un tono senoidal de prueba por el bus, útil para verificar
    /// que la salida física está funcionando sin necesidad de un archivo.
    /// </summary>
    /// <param name="frequencyHz">Frecuencia del tono (por defecto A4 = 440 Hz).</param>
    /// <param name="durationMs">Duración en milisegundos.</param>
    void PlayTestTone(double frequencyHz = 440.0, int durationMs = 1000);

    /// <summary>
    /// Reproduce una secuencia de N pulsos de M ms separados por K ms.
    /// Se usa para el beep de emergencia.
    /// </summary>
    void PlayTestToneSequence(
        double frequencyHz,
        int pulseMs,
        int gapMs,
        int pulseCount,
        double amplitude = 0.75);
}