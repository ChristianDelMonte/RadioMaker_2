namespace RMVoice.Core;

/// <summary>
/// Opciones de inicialización del motor de locución.
/// </summary>
public sealed class AnnouncerOptions
{
    /// <summary>
    /// <c>true</c>  → el Engine llama a <c>Bass.Init()</c> y <c>Bass.Free()</c>.
    ///               Usar en el EXE standalone (RMVoice.exe).
    /// <c>false</c> → el host (RadioMaker) ya inicializó BASS y es dueño de su ciclo de vida.
    ///               El Engine NO toca <c>Bass.Init</c>/<c>Bass.Free</c>.
    /// </summary>
    public bool OwnsBass { get; init; }

    /// <summary>
    /// Ruta al <c>profiles.json</c>. Si es <c>null</c>, usa
    /// <c>%AppData%/RMVoice/profiles.json</c> (o <c>~/.config/RMVoice/</c> en Linux/macOS).
    /// </summary>
    public string? ProfilesPath { get; init; }

    /// <summary>
    /// Ruta al <c>settings.json</c> (ubicación de clima, auto-refresh).
    /// Si es <c>null</c>, usa el mismo directorio que <see cref="ProfilesPath"/>.
    /// </summary>
    public string? SettingsPath { get; init; }

    /// <summary>
    /// Índice de dispositivo por defecto. Se usa cuando
    /// <see cref="AnnounceRequest.DeviceIndex"/> vale <c>-1</c>.
    /// </summary>
    public int DefaultDeviceIndex { get; init; } = -1;

    /// <summary>Volumen inicial del Engine (0.0 – 1.0).</summary>
    public float InitialVolume { get; init; } = 1f;

    /// <summary>
    /// Nombre que se envía en el <c>User-Agent</c> de las consultas al clima.
    /// Útil para que RadioMaker y RMVoice.exe se identifiquen por separado.
    /// </summary>
    public string UserAgent { get; init; } = "RMVoice.Engine/1.0";
}