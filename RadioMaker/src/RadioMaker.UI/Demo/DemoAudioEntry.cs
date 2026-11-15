using System.Text.Json.Serialization;

namespace RadioMaker.UI.Demo;

/// <summary>
/// Un audio de la presentación. Solo el nombre del archivo se persiste
/// en el manifest; la ruta absoluta se resuelve en runtime contra
/// AppContext.BaseDirectory/Assets/demo/.
/// </summary>
public sealed class DemoAudioEntry
{
    /// <summary>Nombre del archivo (ej: "01-bienvenida.mp3"). Solo el nombre, sin carpetas.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    /// <summary>Título opcional para mostrar en el overlay. Si falta, se usa el nombre del archivo.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Ruta absoluta resuelta en runtime. NO se persiste.</summary>
    [JsonIgnore]
    public string? ResolvedPath { get; set; }

    /// <summary>Título a mostrar.</summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title!;
            if (string.IsNullOrWhiteSpace(File)) return "(sin audio)";
            return System.IO.Path.GetFileNameWithoutExtension(File);
        }
    }
}