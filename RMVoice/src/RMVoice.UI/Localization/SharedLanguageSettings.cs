using System.Text.Json;

namespace RMVoice.UI.Localization;

/// <summary>Reads RadioMaker's shared interface-language preference.</summary>
internal static class SharedLanguageSettings
{
    public static string LoadLanguage()
    {
        try
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            var path = Path.Combine(root, "RadioMaker", "language.json");
            if (!File.Exists(path)) return "es";

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Language", out var language) &&
                language.ValueKind == JsonValueKind.String &&
                language.GetString() is "en" or "es")
                return language.GetString()!;
        }
        catch
        {
            // Add-on stays usable if the shared preference is unavailable.
        }

        return "es";
    }
}
