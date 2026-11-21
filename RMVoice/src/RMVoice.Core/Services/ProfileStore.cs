using System.Text.Encodings.Web;
using System.Text.Json;
using System.Diagnostics;
using RMVoice.Core.Models;

namespace RMVoice.Core.Services;

/// -------------
/// Persiste la lista de perfiles de voz en un JSON dentro de AppData.
/// -------------
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;

    /// -------------Crea un store apuntando a <c>%AppData%/RMVoice/profiles.json</c>.
    public ProfileStore() : this(EnginePaths.DefaultProfilesPath()) { }

    /// -------------Crea un store apuntando a una ruta arbitraria.
    public ProfileStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath => _filePath;

    public List<VoiceProfile> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<VoiceProfile>();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<VoiceProfile>>(json, JsonOptions)
                   ?? new List<VoiceProfile>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RMVoice Profiles] No se pudieron cargar perfiles desde '{_filePath}': {ex.Message}");
            return new List<VoiceProfile>();
        }
    }

    public void Save(IEnumerable<VoiceProfile> profiles)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_filePath, JsonSerializer.Serialize(profiles.ToList(), JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RMVoice Profiles] No se pudieron guardar perfiles en '{_filePath}': {ex.Message}");
        }
    }
}
