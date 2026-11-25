using System.Collections;
using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Controls;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.Localization;

/// <summary>Loads localized strings and exposes them as dynamic Avalonia resources.</summary>
public static class LocalizationService
{
    private static readonly ResourceManager ResourceManager = new(
        "RadioMaker.UI.Localization.Strings",
        typeof(LocalizationService).Assembly);

    private static ResourceDictionary? _activeDictionary;

    public static string CurrentLanguage { get; private set; } = "es";

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<UiLanguageOption> Languages { get; } = new[]
    {
        new UiLanguageOption("es", "Español"),
        new UiLanguageOption("en", "English")
    };

    public static void Initialize() => Apply(LanguageSettingsStore.LoadOrCreate(), persist: false);

    public static void SetLanguage(string language) => Apply(language, persist: true);

    public static string GetString(string key)
    {
        try
        {
            return ResourceManager.GetString(key, CultureInfo.GetCultureInfo(CurrentLanguage)) ?? key;
        }
        catch
        {
            return key;
        }
    }

    public static string LocalizeKnownDefault(string value, params string[] resourceKeys)
    {
        if (string.IsNullOrEmpty(value)) return value;

        foreach (var key in resourceKeys)
        {
            var current = GetString(key);
            foreach (var language in new[] { "es", "en" })
            {
                var source = GetStringForLanguage(key, language);
                if (value == source) return current;

                if (TryFormatDefault(value, source, out var argument))
                    return string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), current, argument);
            }
        }

        return value;
    }

    private static string GetStringForLanguage(string key, string language)
    {
        try
        {
            return ResourceManager.GetString(key, CultureInfo.GetCultureInfo(language)) ?? key;
        }
        catch
        {
            return key;
        }
    }

    private static bool TryFormatDefault(string value, string pattern, out int argument)
    {
        argument = 0;
        var placeholder = pattern.IndexOf("{0}", StringComparison.Ordinal);
        if (placeholder < 0) return false;

        var prefix = pattern[..placeholder];
        var suffix = pattern[(placeholder + 3)..];
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !value.EndsWith(suffix, StringComparison.Ordinal) ||
            value.Length < prefix.Length + suffix.Length)
            return false;

        var numberLength = value.Length - prefix.Length - suffix.Length;
        var numberText = value.Substring(prefix.Length, numberLength);
        return int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out argument);
    }

    private static void Apply(string language, bool persist)
    {
        if (language is not ("en" or "es")) language = "es";
        if (language == CurrentLanguage && _activeDictionary is not null) return;

        var culture = CultureInfo.GetCultureInfo(language);
        var resourceSet = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
        if (resourceSet is null) return;

        var dictionary = new ResourceDictionary();
        foreach (DictionaryEntry entry in resourceSet)
        {
            if (entry.Key is not string key) continue;
            dictionary[key] = ResourceManager.GetString(key, culture) ?? entry.Value ?? string.Empty;
        }

        var application = Application.Current;
        if (application is null) return;

        var merged = application.Resources.MergedDictionaries;
        if (_activeDictionary is not null) merged.Remove(_activeDictionary);
        merged.Add(dictionary);
        _activeDictionary = dictionary;
        CurrentLanguage = language;

        if (persist) LanguageSettingsStore.Save(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}

public sealed record UiLanguageOption(string Code, string DisplayName);
