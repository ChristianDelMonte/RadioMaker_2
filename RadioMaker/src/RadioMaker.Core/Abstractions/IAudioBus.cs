namespace RadioMaker.Core.Abstractions;

/// -------------
/// Un Bus de audio representa una salida física independiente con su propio
/// dispositivo BASS, su propio volumen master y su propio conjunto de canales.
///
/// Ejemplos típicos de uso futuro:
///   • Bus "Aire"   → salida al transmisor/altavoces principales.
///   • Bus "Monitor"→ auriculares del operador.
///   • Bus "Preview"→ altavoces de estudio para preescucha.
/// -------------
public interface IAudioBus : IDisposable
{
    /// -------------Identificador inmutable del bus.</summary>
    string Id { get; }

    /// -------------Indica si este es el bus predeterminado para reproducción sin ruta explícita.</summary>
    bool IsPrimary => false;

    /// -------------Nombre visible, editable en tiempo de ejecucion.</summary>
    string DisplayName { get; set; }

    /// -------------Indice del dispositivo BASS asociado (‑1 = predeterminado).</summary>
    int DeviceIndex { get; }

    /// -------------
    /// Indice REAL del dispositivo tras resolver -1 al default del sistema.
    /// Es el que usa BASS para operaciones como ChannelSetDevice.
    /// Se usa para que RMVoice (locuciones) reproduzca en el MISMO device
    /// que el audio del bus.
    /// -------------
    int ResolvedDeviceIndex { get; }

    /// -------------
    /// Nombre legible del dispositivo fisico asociado al bus.
    /// Ej: "Altavoces (Realtek High Definition Audio)".
    /// Si el bus usa el dispositivo predeterminado, describe eso.
    /// Se usa para logging y para mostrar en la UI.
    /// -------------
    string DeviceDisplayName { get; }

    /// -------------Volumen master del bus (0.0 .. 1.0). Aplica a todos los canales del bus.</summary>
    double MasterVolume { get; set; }

    /// -------------Indica si el bus fue inicializado correctamente.</summary>
    bool IsInitialized { get; }

    /// -------------
    /// Cantidad de canales ACTIVOS (State == Playing) en este bus.
    /// Util para el watchdog de emergencia que detecta "bus vacio".
    /// -------------
    int ActiveChannelCount { get; }

    /// -------------
    /// Niveles actuales agregados del bus (max peak entre todos los canales
    /// activos). Se usa para el watchdog de emergencia que detecta silencio
    /// digital aunque el canal exista.
    /// -------------
    AudioLevels GetCurrentLevels();

    /// -------------
    /// Inicializa el bus contra su dispositivo de salida. Es llamado
    /// automáticamente por <see cref="IAudioEngine.CreateBus"/> y no suele
    /// invocarse directamente.
    /// -------------
    void Initialize();

    /// -------------
    /// Cambia el dispositivo de salida del bus. Libera los canales activos
    /// (BASS requiere Free + Init y eso invalida los handles existentes).
    /// -------------
    bool ChangeDevice(int deviceIndex);

    /// -------------
    /// Carga un archivo de audio en este bus y devuelve un canal reproducible.
    /// -------------
    IAudioChannel LoadFile(string filePath);

    /// -------------
    /// Reproduce un tono senoidal de prueba por el bus, útil para verificar
    /// que la salida física está funcionando sin necesidad de un archivo.
    /// -------------
    /// <param name="frequencyHz">Frecuencia del tono (por defecto A4 = 440 Hz).</param>
    /// <param name="durationMs">Duración en milisegundos.</param>
    void PlayTestTone(double frequencyHz = 440.0, int durationMs = 1000);

    /// -------------
    /// Reproduce una secuencia de N pulsos de M ms separados por K ms.
    /// Se usa para el beep de emergencia.
    /// -------------
    void PlayTestToneSequence(
        double frequencyHz,
        int pulseMs,
        int gapMs,
        int pulseCount,
        double amplitude = 0.75);
}
