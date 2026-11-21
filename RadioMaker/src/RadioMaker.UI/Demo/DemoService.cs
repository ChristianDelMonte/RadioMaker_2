using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioMaker.UI.Demo;

/// -------------
/// Reproduce la secuencia de audios de la presentación inicial.
///
/// Aislamiento: no toca PlaylistPlayer ni PlaybackEngine. Usa el
/// PrimaryBus directamente. Respeta engine.GlobalVolume automáticamente
/// (BassBus.LoadFile aplica el global volume al canal nuevo).
/// -------------
public sealed class DemoService : IDisposable
{
    private const string DemoFolderRelative = "Assets/demo";
    private const string ManifestFileName = "manifest.json";

    private readonly IAudioEngine _engine;
    private readonly List<DemoAudioEntry> _audios = new();
    private CancellationTokenSource? _cts;
    private IAudioChannel? _currentChannel;
    private bool _disposed;

    public IReadOnlyList<DemoAudioEntry> Audios => _audios;
    public bool IsLoaded => _audios.Count > 0;
    public int CurrentIndex { get; private set; } = -1;
    public DemoAudioEntry? CurrentEntry =>
        CurrentIndex >= 0 && CurrentIndex < _audios.Count ? _audios[CurrentIndex] : null;

    public event Action<DemoAudioEntry, int, int>? AudioStarted;
    public event Action? PlaybackCompleted;
    public event Action<string>? Failed;

    public DemoService(IAudioEngine engine) => _engine = engine;

    /// -------------Devuelve el canal del audio en curso (o null).</summary>
    public IAudioChannel? GetCurrentChannel() => _currentChannel;

    /// -------------Carga el manifest y resuelve las rutas. Devuelve false si no hay nada que reproducir.</summary>
    public bool TryLoad()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var demoDir = Path.Combine(baseDir, "Assets", "demo");
            var manifestPath = Path.Combine(demoDir, ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                AppLogger.Info("DEMO", $"Manifest no encontrado: {manifestPath}");
                return false;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<DemoManifest>(json);

            if (manifest?.Audios is null || manifest.Audios.Count == 0)
            {
                AppLogger.Warning("DEMO", "Manifest vacío o inválido.");
                return false;
            }

            _audios.Clear();
            foreach (var entry in manifest.Audios)
            {
                if (string.IsNullOrWhiteSpace(entry.File)) continue;

                var path = Path.Combine(demoDir, entry.File);
                if (!File.Exists(path))
                {
                    AppLogger.Warning("DEMO", $"Audio no encontrado, se salta: {entry.File}");
                    continue;
                }

                entry.ResolvedPath = path;
                _audios.Add(entry);
            }

            if (_audios.Count == 0)
            {
                AppLogger.Warning("DEMO", "Ningún audio válido en el manifest.");
                return false;
            }

            AppLogger.Info("DEMO", $"{_audios.Count} audio(s) listo(s) para la presentación.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DEMO", "Error cargando manifest", ex);
            return false;
        }
    }

    /// -------------Reproduce toda la secuencia. Await hasta que termine o el usuario haga Skip.</summary>
    public async Task PlayAsync(CancellationToken externalCt = default)
    {
        if (!IsLoaded) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        try
        {
            for (int i = 0; i < _audios.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                CurrentIndex = i;
                var entry = _audios[i];

                AudioStarted?.Invoke(entry, i + 1, _audios.Count);
                AppLogger.Info("DEMO", $"Reproduciendo {i + 1}/{_audios.Count}: {entry.DisplayTitle}");

                try
                {
                    await PlayOneAsync(entry, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLogger.Warning("DEMO",
                        $"Error en audio {i + 1} '{entry.File}': {ex.Message}. Se salta.");
                    // Continuar con el siguiente
                }
            }

            PlaybackCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Skip del usuario. Fin normal.
        }
        catch (Exception ex)
        {
            AppLogger.Error("DEMO", "Error reproduciendo secuencia", ex);
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            _currentChannel = null;
            CurrentIndex = -1;
        }
    }

    private async Task PlayOneAsync(DemoAudioEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.ResolvedPath))
        {
            AppLogger.Warning("DEMO", $"Skip '{entry.File}': ruta vacía.");
            return;
        }

        var bus = _engine.PrimaryBus;
        var channel = bus.LoadFile(entry.ResolvedPath);
        _currentChannel = channel;

        var durSecs = channel.Duration.TotalSeconds;
        AppLogger.Info("DEMO",
            $"Cargado '{entry.DisplayTitle}' (duración: {durSecs:F1}s)");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.Ended += (_, e) => tcs.TrySetResult(!e.WasStoppedByUser);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        // Chequear retorno de Play() — si falla, saltar en lugar de colgar.
        var playOk = channel.Play();
        if (!playOk)
        {
            AppLogger.Warning("DEMO",
                $"Play() devolvió false para '{entry.File}'. Se salta.");
            try { channel.Stop(); } catch { }
            try { channel.Dispose(); } catch { }
            _currentChannel = null;
            return;
        }

        // Timeout de seguridad: duración + 5s. Si el audio no terminó
        // (device ocupado, archivo corrupto, etc), forzar skip.
        if (durSecs <= 0.1) durSecs = 60.0; // fallback si Duration es 0
        var timeoutMs = (int)Math.Ceiling(durSecs * 1000 + 5000);

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var timeoutReg = timeoutCts.Token.Register(() =>
        {
            AppLogger.Warning("DEMO",
                $"Timeout en '{entry.File}' después de {timeoutMs / 1000}s. Se salta.");
            tcs.TrySetException(new TimeoutException(
                $"Timeout reproduciendo '{entry.File}'"));
        });

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            try { channel.Stop(); } catch { }
            try { channel.Dispose(); } catch { }
            _currentChannel = null;
        }
    }

    public void Pause()
    {
        try { _currentChannel?.Pause(); } catch { }
    }

    public void Resume()
    {
        try { _currentChannel?.Play(); } catch { }
    }

    public void Skip()
    {
        try { _cts?.Cancel(); } catch { }
        try { _currentChannel?.Stop(); } catch { }
    }

    public void Stop() => Skip();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        try { _currentChannel?.Stop(); } catch { }
        try { _currentChannel?.Dispose(); } catch { }

        _currentChannel = null;
        _cts?.Dispose();
        _cts = null;
    }
}