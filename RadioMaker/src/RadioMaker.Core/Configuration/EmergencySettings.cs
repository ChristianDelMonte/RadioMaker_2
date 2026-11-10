namespace RadioMaker.Core.Configuration;

public enum EmergencySourceMode
{
    /// <summary>Playlist pre-armada (.rmf) unicamente.</summary>
    Prearmed = 0,
    /// <summary>Carpeta con audios, shuffle.</summary>
    Folder = 1,
    /// <summary>Playlist pre-armada si existe, carpeta como fallback.</summary>
    Hybrid = 2
}

public sealed class EmergencySettings
{
    public int Version { get; set; } = 1;

    // ───── Activacion global ─────
    public bool IsEnabled { get; set; } = false;

    // ───── Deteccion ─────
    public int SilenceThresholdSeconds { get; set; } = 30;
    public double ThresholdDb { get; set; } = -65.0;
    public bool DetectNoChannels { get; set; } = true;
    public bool DetectSilence { get; set; } = true;

    // ───── Origen ─────
    public EmergencySourceMode Mode { get; set; } = EmergencySourceMode.Hybrid;
    public string PrearmedPlaylistPath { get; set; } = "";
    public string EmergencyFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = false;

    // ───── Playback ─────
    public double Volume { get; set; } = 0.8;
    public int FadeInMs { get; set; } = 2000;
    public int FadeOutMs { get; set; } = 500;

    // ───── Beep ─────
    public bool BeepEnabled { get; set; } = true;
    public int BeepEveryNTracks { get; set; } = 2;
    public int BeepFrequencyHz { get; set; } = 800;
    public int BeepDurationMs { get; set; } = 500;

    public EmergencySettings Clone() => new()
    {
        Version = Version,
        IsEnabled = IsEnabled,
        SilenceThresholdSeconds = SilenceThresholdSeconds,
        ThresholdDb = ThresholdDb,
        DetectNoChannels = DetectNoChannels,
        DetectSilence = DetectSilence,
        Mode = Mode,
        PrearmedPlaylistPath = PrearmedPlaylistPath,
        EmergencyFolder = EmergencyFolder,
        IncludeSubfolders = IncludeSubfolders,
        Volume = Volume,
        FadeInMs = FadeInMs,
        FadeOutMs = FadeOutMs,
        BeepEnabled = BeepEnabled,
        BeepEveryNTracks = BeepEveryNTracks,
        BeepFrequencyHz = BeepFrequencyHz,
        BeepDurationMs = BeepDurationMs
    };
}