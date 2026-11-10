using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Playback;

namespace RadioMaker.UI.Configuration;

public sealed partial class InterruptionBehaviorSettings : ObservableObject
{
    [ObservableProperty] private PlaylistInterruptionBehavior _behavior = PlaylistInterruptionBehavior.Continue;

    public PlaylistInterruptionBehavior[] AvailableBehaviors { get; } =
        { PlaylistInterruptionBehavior.Continue, PlaylistInterruptionBehavior.SkipToNext };
}

public static class InterruptionBehaviorSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static InterruptionBehaviorSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<InterruptionBehaviorSettings>(json, Options);
                if (s is not null) return s;
            }
        }
        catch { }
        return new InterruptionBehaviorSettings();
    }

    public static void Save(InterruptionBehaviorSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "interruption.json");
    }
}