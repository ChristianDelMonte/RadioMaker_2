using System;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace RMWave.UI.Localization;

/// <summary>Persists RMWave's independent interface-language preference.</summary>
internal static class SharedLanguageSettings
{
    private static string SettingsPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            return Path.Combine(root, "RMWave", "ui-language.json");
        }
    }

    public static string LoadLanguage()
    {
        string? configured = null;
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (document.RootElement.TryGetProperty("Language", out var language) &&
                    language.ValueKind == JsonValueKind.String && language.GetString() is "en" or "es")
                    configured = language.GetString();
            }
        }
        catch { }

        if (configured is not null) return configured;

        var detected = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (detected is not ("en" or "es")) detected = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        var initial = detected == "es" ? "es" : "en";
        SaveLanguage(initial);
        return initial;
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
