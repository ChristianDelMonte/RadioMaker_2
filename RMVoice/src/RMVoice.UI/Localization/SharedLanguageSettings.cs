using System.Text.Json;
using System.Globalization;

namespace RMVoice.UI.Localization;

/// <summary>Persists RMVoice's independent interface-language preference.</summary>
internal static class SharedLanguageSettings
{
    private static string SettingsPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            return Path.Combine(root, "RMVoice", "ui-language.json");
        }
    }

    public static string LoadLanguage()
    {
        string? configured = null;
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("Language", out var language) &&
                    language.ValueKind == JsonValueKind.String &&
                    language.GetString() is "en" or "es")
                    configured = language.GetString();
            }
        }
        catch
        {
            // Add-on stays usable if the shared preference is unavailable.
        }

        if (configured is not null) return configured;

        var detected = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (detected is not ("en" or "es")) detected = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        var initialLanguage = detected == "es" ? "es" : "en";
        SaveLanguage(initialLanguage);
        return initialLanguage;
    }

    public static void SaveLanguage(string language)
    {
        if (language is not ("en" or "es")) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { Language = language }));
        }
        catch { }
    }
}
