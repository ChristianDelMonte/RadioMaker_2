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

    public static CultureInfo CurrentCulture => _culture;

    public static string GetString(string key) => ResourceManager.GetString(key, _culture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(_culture, GetString(key), args);

    public static void Initialize()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(SharedLanguageSettings.LoadLanguage());
            _culture = culture;
            var set = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
            var application = Application.Current;
            if (set is null || application is null) return;

            var dictionary = new ResourceDictionary();
            foreach (DictionaryEntry entry in set)
            {
                if (entry.Key is string key)
                    dictionary[key] = ResourceManager.GetString(key, culture) ?? entry.Value ?? string.Empty;
            }

            application.Resources.MergedDictionaries.Add(dictionary);
        }
        catch
        {
            // Localization is optional; it must not prevent RMVoice startup.
        }
    }
}
