using System.Globalization;
using System.Text.Json;

namespace RadioMaker.UI.Configuration;

/// <summary>Persists the operator's interface language choice.</summary>
public static class LanguageSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigPath { get; } = BuildPath();

    public static string LoadOrCreate()
    {
        if (TryLoad(out var language)) return language;

        var detectedLanguage = DetectSystemLanguage();
        Save(detectedLanguage);
        return detectedLanguage;
    }

    public static bool TryLoad(out string language)
    {
        language = "";
        try
        {
            if (!File.Exists(ConfigPath)) return false;
            var settings = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(ConfigPath), JsonOptions);
            if (settings?.Language is not ("en" or "es")) return false;
            language = settings.Language;
            return true;
        }
        catch
        {
            // A missing or unreadable preference is treated as a first launch.
            return false;
        }
    }

    public static string DetectSystemLanguage()
    {
        // .NET gets the current UI culture from the host OS on Windows, macOS, and Linux.
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (language is not ("en" or "es"))
            language = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

        return language == "es" ? "es" : "en";
    }

    public static void Save(string language)
    {
        if (language is not ("en" or "es")) return;
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new LanguageSettings { Language = language }, JsonOptions));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "language.json");
    }

    private sealed class LanguageSettings
    {
        public int Version { get; set; } = 1;
        public string Language { get; set; } = "";
    }
}
