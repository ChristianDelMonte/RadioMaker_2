using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.Core.Engine;

/// -------------
/// Procesador de ducking automatico.
///
/// Responsabilidades:
///   - Capturar el audio del microfono con Bass.RecordInit + Bass.RecordStart.
///   - Medir el nivel RMS del microfono en cada callback (hilo interno de BASS).
///   - Ejecutar una maquina de estados que calcula la ganancia de ducking:
///       * Si el nivel supera el umbral -> reducir ganancia con attack.
///       * Si el nivel cae -> mantener durante HoldMs.
///       * Pasado el hold -> restaurar ganancia con release.
///   - Notificar al engine (via callback) cada cambio de ganancia.
///
/// El calculo de ganancia corre en un timer interno a 50 Hz (cada 20 ms).
/// El callback del microfono solo publica el ultimo nivel medido.
///
/// Threading:
///   - _currentMicLevel y _currentGainDb cruzan hilos con accesos Interlocked.
///   - _config y la maquina de estados del timer estan protegidas con _timerLock.
/// -------------
internal sealed class DuckingProcessor : IDisposable
{
    // ───── Configuracion de captura ──────────────────────────────────────
    private const int MicFreq = 44100;
    private const int MicChannels = 2;
    private const int UpdateIntervalMs = 20; // 50 Hz

    // ───── Estado del microfono (hilo BASS) ──────────────────────────────
    private volatile bool _micInitialized;
    private volatile int _micStreamHandle;
    private double _currentMicLevel; // rango 0..1 lineal, actualizado por el record callback
    private readonly RecordProcedure _recordProc;
    /// -------------Buffer managed reutilizable para copiar los samples del micrófono.</summary>
    private float[] _recordBuffer = Array.Empty<float>();

    // ───── Estado del timer de procesamiento ─────────────────────────────
    private readonly object _timerLock = new();
    private readonly object _lifecycleLock = new();
    private System.Threading.Timer? _timer;

    private DuckingConfig _config = new();
    private volatile bool _enabled;

    // Maquina de estados de la ganancia
    private double _currentGainDb;     // ganancia de ducking actual (0 = sin ducking, -X dB = atenuado)
    private int _holdCounterMs;        // ms acumulados desde la ultima deteccion de voz

    // ───── Callback al engine ────────────────────────────────────────────
    private readonly Action<double> _onGainChanged;

    // ───── Publico ───────────────────────────────────────────────────────
    public bool IsActive => _enabled && _micInitialized;

    /// -------------Nivel actual del microfono en dBFS (rango tipico -80 a 0).</summary>
    public double CurrentMicLevelDb
    {
        get
        {
            double level = Interlocked.CompareExchange(ref _currentMicLevel, 0.0, 0.0);
            if (level <= 1e-9) return -120.0;
            return 20.0 * Math.Log10(level);
        }
    }

    /// -------------Ganancia de ducking actual (dB, siempre &lt;= 0).</summary>
    public double CurrentGainDb => Interlocked.CompareExchange(ref _currentGainDb, 0.0, 0.0);

    /// -------------Configuracion actual (copia).</summary>
    public DuckingConfig Config
    {
        get { lock (_timerLock) return _config.Clone(); }
    }

    public DuckingProcessor(Action<double> onGainChanged)
    {
        _onGainChanged = onGainChanged ?? throw new ArgumentNullException(nameof(onGainChanged));
        _recordProc = OnRecordData;
    }

    // ---------------------------------------------------------------- Control

    /// -------------
    /// Inicia o actualiza el procesador con la configuracion dada.
    /// Si el procesador ya estaba corriendo, se aplican los cambios en vivo.
    /// -------------
    public void Start(DuckingConfig config)
    {
        if (config is null) return;

        lock (_lifecycleLock)
            StartCore(config);
    }

    private void StartCore(DuckingConfig config)
    {
        var appliedConfig = config.Clone();
        bool deviceChanged;

        // Publicar la copia bajo el mismo lock que usa el timer para leerla.
        lock (_timerLock)
        {
            deviceChanged = !_micInitialized || appliedConfig.MicDeviceIndex != _config.MicDeviceIndex;
            _config = appliedConfig;
            _enabled = appliedConfig.IsEnabled;
        }

        if (!_enabled)
        {
            StopCore();
            return;
        }

        if (deviceChanged)
        {
            StopMic();
            if (!StartMic(appliedConfig.MicDeviceIndex))
            {
                StopCore();
                return;
            }
        }

        // Arrancar (o reiniciar) el timer de procesamiento.
        lock (_timerLock)
        {
            _timer ??= new System.Threading.Timer(
                OnTimerTick, null, UpdateIntervalMs, UpdateIntervalMs);
        }

        _onGainChanged(CurrentGainDb);
    }

    /// -------------Detiene el procesador y libera el microfono.</summary>
    public void Stop()
    {
        lock (_lifecycleLock)
            StopCore();
    }

    private void StopCore()
    {
        lock (_timerLock)
        {
            _enabled = false;
            Interlocked.Exchange(ref _currentGainDb, 0.0);
            _holdCounterMs = 0;
            _timer?.Dispose();
            _timer = null;
        }

        StopMic();
        _onGainChanged(0.0);
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- Microfono

    private bool StartMic(int deviceIndex)
    {
        try
        {
            // Inicializar el dispositivo de entrada.
            // Bass.RecordInit(device) — device -1 = predeterminado, -2 = first enabled
            int resolvedDevice = ResolveMicDeviceIndex(deviceIndex);

            if (!Bass.RecordInit(resolvedDevice))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"No se pudo inicializar el microfono {resolvedDevice}: {Bass.LastError}");
                return false;
            }

            // Capturar el stream de entrada en formato float para facilitar el analisis.
            int handle = Bass.RecordStart(
                MicFreq,
                MicChannels,
                BassFlags.Float,
                _recordProc,
                IntPtr.Zero);

            if (handle == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Bass.RecordStart fallo: {Bass.LastError}");
                Bass.RecordFree();
                return false;
            }

            _micStreamHandle = handle;
            _micInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Excepcion iniciando microfono: {ex.Message}");
            _micInitialized = false;
            return false;
        }
    }

    private void StopMic()
    {
        if (!_micInitialized) return;

        try
        {
            if (_micStreamHandle != 0)
            {
                Bass.ChannelStop(_micStreamHandle);
                Bass.StreamFree(_micStreamHandle);
                _micStreamHandle = 0;
            }
            Bass.RecordFree();
        }
        catch { }

        _micInitialized = false;
        Interlocked.Exchange(ref _currentMicLevel, 0.0);
    }

    private static int ResolveMicDeviceIndex(int requested)
    {
        // En BASS, -1 significa "no sound" para salida pero para entrada
        // el pseudo-dispositivo predeterminado es -1 tambien. Sin embargo,
        // hay que verificar que exista al menos un dispositivo.
        if (requested >= 0 && requested < Bass.RecordingDeviceCount)
            return requested;

        // Buscar el predeterminado.
        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            if (Bass.RecordGetDeviceInfo(i, out var info) && info.IsDefault && info.IsEnabled)
                return i;
        }

        // Fallback: primer dispositivo habilitado.
        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            if (Bass.RecordGetDeviceInfo(i, out var info) && info.IsEnabled)
                return i;
        }

        return 0;
    }

    // ---------------------------------------------------------------- Record callback

    /// -------------
    /// Callback invocado por BASS en un hilo interno cuando hay datos del
    /// microfono disponibles. Calcula el RMS del bloque y actualiza
    /// _currentMicLevel.
    ///
    /// RecordProcedure en ManagedBass 4.x devuelve bool:
    ///   - true  => seguir capturando
    ///   - false => detener la captura
    ///
    /// Se usa Marshal.Copy para leer el buffer nativo sin bloques unsafe.
    /// -------------
    private bool OnRecordData(int handle, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0 || buffer == IntPtr.Zero) return true;

        int samplesCount = length / sizeof(float);
        if (samplesCount <= 0) return true;

        // Buffer managed reutilizable. Solo se redimensiona cuando llega un
        // bloque mas grande que el anterior.
        if (_recordBuffer.Length < samplesCount)
            _recordBuffer = new float[samplesCount];

        // Copiar del buffer nativo al managed array.
        Marshal.Copy(buffer, _recordBuffer, 0, samplesCount);

        double sumSq = 0.0;
        for (int i = 0; i < samplesCount; i++)
        {
            double s = _recordBuffer[i];
            sumSq += s * s;
        }

        double rms = Math.Sqrt(sumSq / samplesCount);
        Interlocked.Exchange(ref _currentMicLevel, rms);

        return true; // continuar capturando
    }

    // ---------------------------------------------------------------- Maquina de estados

    /// -------------
    /// Timer interno a 50 Hz. Calcula la ganancia objetivo y la interpola
    /// suavemente hacia la actual (attack/release). Aplica el resultado
    /// via _onGainChanged.
    /// -------------
    private void OnTimerTick(object? state)
    {
        double micDb;
        DuckingConfig cfg;
        double gainDb;

        lock (_timerLock)
        {
            if (!_enabled) return;

            cfg = _config;
            micDb = CurrentMicLevelDb;

            bool voiceDetected = micDb > cfg.ThresholdDb;

            // Determinar ganancia objetivo segun deteccion y hold.
            double targetGainDb;
            if (voiceDetected)
            {
                // Voz presente: resetear hold y atenuar.
                _holdCounterMs = 0;
                targetGainDb = -cfg.ReductionDb;
            }
            else
            {
                // Sin voz: incrementar hold.
                _holdCounterMs += UpdateIntervalMs;

                if (_holdCounterMs < cfg.HoldMs)
                {
                    // Mantener atenuacion durante el hold.
                    targetGainDb = -cfg.ReductionDb;
                }
                else
                {
                    // Hold expirado: restaurar.
                    targetGainDb = 0.0;
                }
            }

            // Interpolar la ganancia actual hacia el target usando attack o release.
            double currentGainDb = CurrentGainDb;
            double diff = targetGainDb - currentGainDb;
            if (Math.Abs(diff) < 0.05)
            {
                currentGainDb = targetGainDb;
            }
            else
            {
                // Cuanto mas "rapido" el tiempo, mas grande el factor de interpolacion.
                int rateMs = diff < 0 ? cfg.AttackMs : cfg.ReleaseMs;
                if (rateMs <= 0) rateMs = 1;

                double step = Math.Min(1.0, (double)UpdateIntervalMs / rateMs);
                currentGainDb += diff * step;
            }

            Interlocked.Exchange(ref _currentGainDb, currentGainDb);
            gainDb = currentGainDb;
        }

        // No publicar un cálculo que haya quedado obsoleto por un Stop/Start.
        if (_enabled && Math.Abs(CurrentGainDb - gainDb) < 0.0001)
            _onGainChanged(gainDb);
    }
}
