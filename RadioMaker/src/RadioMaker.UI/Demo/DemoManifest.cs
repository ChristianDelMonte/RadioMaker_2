using System.Text.Json.Serialization;

namespace RadioMaker.UI.Demo;

/// -------------
/// Modelo del manifest.json que describe la presentación inicial.
/// Vive en Assets/demo/manifest.json dentro del publish.
/// -------------
public sealed class DemoManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("audios")]
    public List<DemoAudioEntry> Audios { get; set; } = new();
}