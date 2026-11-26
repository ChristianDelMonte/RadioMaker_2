using System.Collections;
using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Controls;

namespace RMVoice.UI.Localization;

internal static class VoiceLocalizationService
{
    private static readonly ResourceManager ResourceManager = new(
        "RMVoice.UI.Localization.Strings",
        typeof(VoiceLocalizationService).Assembly);

    private static CultureInfo _culture = CultureInfo.GetCultureInfo("es");
    private static ResourceDictionary? _activeDictionary;

    public static CultureInfo CurrentCulture => _culture;
    public static string CurrentLanguage => _culture.TwoLetterISOLanguageName;
    public static event EventHandler? LanguageChanged;

    public static string GetString(string key) => ResourceManager.GetString(key, _culture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(_culture, GetString(key), args);

    public static void Initialize()
    {
        ApplyLanguage(SharedLanguageSettings.LoadLanguage(), persist: false);
    }

    public static void SetLanguage(string language) => ApplyLanguage(language, persist: true);

    private static void ApplyLanguage(string language, bool persist)
    {
        try
        {
            if (language is not ("en" or "es")) language = "en";
            var culture = CultureInfo.GetCultureInfo(language);
            var set = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
            var application = Application.Current;
            _culture = culture;
            if (application is null)
            {
                if (persist) SharedLanguageSettings.SaveLanguage(language);
                return;
            }
            if (set is null) return;

            var dictionary = new ResourceDictionary();
            foreach (DictionaryEntry entry in set)
            {
                if (entry.Key is string key)
                    dictionary[key] = ResourceManager.GetString(key, culture) ?? entry.Value ?? string.Empty;
            }

            if (_activeDictionary is not null)
                application.Resources.MergedDictionaries.Remove(_activeDictionary);
            application.Resources.MergedDictionaries.Add(dictionary);
            _activeDictionary = dictionary;
            if (persist) SharedLanguageSettings.SaveLanguage(language);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // Localization is optional; it must not prevent RMVoice startup.
        }
    }
}
