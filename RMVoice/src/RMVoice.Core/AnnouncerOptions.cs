namespace RMVoice.Core;

/// -------------
/// Opciones de inicialización del motor de locución.
/// -------------
public sealed class AnnouncerOptions
{
    /// <summary>Interface language used to select the bundled default voice assets.</summary>
    public string InterfaceLanguage { get; init; } = "es";

    /// -------------
    /// <c>true</c>  → el Engine llama a <c>Bass.Init()</c> y <c>Bass.Free()</c>.
    ///               Usar en el EXE standalone (RMVoice.exe).
    /// <c>false</c> → el host (RadioMaker) ya inicializó BASS y es dueño de su ciclo de vida.
    ///               El Engine NO toca <c>Bass.Init</c>/<c>Bass.Free</c>.
    /// -------------
    public bool OwnsBass { get; init; }

    /// -------------
    /// Ruta al <c>profiles.json</c>. Si es <c>null</c>, usa
    /// <c>%AppData%/RMVoice/profiles.json</c> (o <c>~/.config/RMVoice/</c> en Linux/macOS).
    /// -------------
    public string? ProfilesPath { get; init; }

    /// -------------
    /// Ruta al <c>settings.json</c> (ubicación de clima, auto-refresh).
    /// Si es <c>null</c>, usa el mismo directorio que <see cref="ProfilesPath"/>.
    /// -------------
    public string? SettingsPath { get; init; }

    /// -------------
    /// Índice de dispositivo por defecto. Se usa cuando
    /// <see cref="AnnounceRequest.DeviceIndex"/> vale <c>-1</c>.
    /// -------------
    public int DefaultDeviceIndex { get; init; } = -1;

    /// -------------Volumen inicial del Engine (0.0 – 1.0).
    public float InitialVolume { get; init; } = 1f;

    /// -------------
    /// Nombre que se envía en el <c>User-Agent</c> de las consultas al clima.
    /// Útil para que RadioMaker y RMVoice.exe se identifiquen por separado.
    /// -------------
    public string UserAgent { get; init; } = "RMVoice.Engine/1.0";
}
