using System.Text.Encodings.Web;
using System.Text.Json;
using RMVoice.Core.Models;

namespace RMVoice.Core.Services;

/// -------------
/// Persiste los ajustes globales de la app en %AppData%/RMVoice/settings.json.
/// -------------
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;

    /// -------------Crea un store apuntando a <c>%AppData%/RMVoice/settings.json</c>.
    public AppSettingsStore() : this(EnginePaths.DefaultSettingsPath()) { }

    /// -------------Crea un store apuntando a una ruta arbitraria.
    public AppSettingsStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Silencioso: no queremos tumbar la app si no se puede escribir el JSON.
        }
    }
}