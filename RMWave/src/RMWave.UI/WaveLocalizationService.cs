using System;
using System.Collections;
using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Controls;

namespace RMWave.UI.Localization;

internal static class WaveLocalizationService
{
    private static readonly ResourceManager ResourceManager = new(
        "RMWave.UI.Strings",
        typeof(WaveLocalizationService).Assembly);

    private static CultureInfo _culture = CultureInfo.GetCultureInfo("es");

    public static CultureInfo CurrentCulture => _culture;

    public static string GetString(string key) => ResourceManager.GetString(key, _culture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(_culture, GetString(key), args);

    public static string TranslateExportStatus(string status)
    {
        if (_culture.TwoLetterISOLanguageName != "en") return status;

        if (status == "Error creando mixer de renderizado.") return GetString("Loc.RMWave.Export.MixerError");
        if (status == "El proyecto está vacío.") return GetString("Loc.RMWave.Export.EmptyProject");
        if (status == "Exportación completada con éxito.") return GetString("Loc.RMWave.Export.Completed");
        if (status == "Advertencia: El archivo no fue creado.") return GetString("Loc.RMWave.Export.FileNotCreated");
        if (status == "Rango inválido.") return GetString("Loc.RMWave.Export.InvalidRange");
        if (status == "Exportación de selección completada.") return GetString("Loc.RMWave.Export.SelectionCompleted");

        const string missingDirectory = "Error: El directorio no existe: ";
        if (status.StartsWith(missingDirectory, StringComparison.Ordinal))
            return Format("Loc.RMWave.Export.MissingDirectory", status[missingDirectory.Length..]);

        const string encoderPrefix = "Error iniciando encoder MP3 (código: ";
        if (status.StartsWith(encoderPrefix, StringComparison.Ordinal) && status.EndsWith(").", StringComparison.Ordinal))
            return Format("Loc.RMWave.Export.EncoderError", status[encoderPrefix.Length..^2]);

        const string mp3DonePrefix = "Exportación MP3 completada. Tamaño: ";
        if (status.StartsWith(mp3DonePrefix, StringComparison.Ordinal) && status.EndsWith(" KB.", StringComparison.Ordinal))
            return Format("Loc.RMWave.Export.Mp3Completed", status[mp3DonePrefix.Length..^4]);

        foreach (var (prefix, key) in new[]
        {
            ("Error exportando selección MP3: ", "Loc.RMWave.Export.SelectionMp3Error"),
            ("Error exportando selección: ", "Loc.RMWave.Export.SelectionError"),
            ("Error exportando MP3: ", "Loc.RMWave.Export.Mp3Error"),
            ("Error exportando: ", "Loc.RMWave.Export.Error")
        })
        {
            if (status.StartsWith(prefix, StringComparison.Ordinal))
                return Format(key, status[prefix.Length..]);
        }

        return status;
    }

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
            // The add-on must remain usable if the shared preference cannot be loaded.
        }
    }
}
