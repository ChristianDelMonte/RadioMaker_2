using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.UI.Controls;

namespace RadioMaker.UI.Configuration;

public sealed partial class VuMeterSettings : ObservableObject
{
    [ObservableProperty] private VuMeterStyle _style = VuMeterStyle.LedSegments;
    [ObservableProperty] private VuMeterOrientation _orientation = VuMeterOrientation.Vertical;
    [ObservableProperty] private bool _showDbScale = true;
    [ObservableProperty] private int _segmentCount = 24;
    [ObservableProperty] private bool _showPeakHold = true;
    [ObservableProperty] private bool _showRms = false;
    [ObservableProperty] private bool _showClipIndicator = true;
    [ObservableProperty] private double _barWidth = 26.0;
    [ObservableProperty] private int _peakHoldDecayMs = 1500;

    [ObservableProperty] private string _greenZoneColor = "#10B981";
    [ObservableProperty] private string _amberZoneColor = "#F59E0B";
    [ObservableProperty] private string _redZoneColor = "#EF4444";
    [ObservableProperty] private string _backgroundColor = "#1A1A1A";
    [ObservableProperty] private string _scaleTextColor = "#9CA3AF";
    [ObservableProperty] private string _accentColor = "#4D6BFE";

    [JsonIgnore] public Color GreenColor => SafeParse(GreenZoneColor);
    [JsonIgnore] public Color AmberColor => SafeParse(AmberZoneColor);
    [JsonIgnore] public Color RedColor => SafeParse(RedZoneColor);
    [JsonIgnore] public Color BgColor => SafeParse(BackgroundColor);
    [JsonIgnore] public Color ScaleColor => SafeParse(ScaleTextColor);
    [JsonIgnore] public Color Accent => SafeParse(AccentColor);

    private static Color SafeParse(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Colors.Gray; }
    }

    partial void OnGreenZoneColorChanged(string value) => OnPropertyChanged(nameof(GreenColor));
    partial void OnAmberZoneColorChanged(string value) => OnPropertyChanged(nameof(AmberColor));
    partial void OnRedZoneColorChanged(string value) => OnPropertyChanged(nameof(RedColor));
    partial void OnBackgroundColorChanged(string value) => OnPropertyChanged(nameof(BgColor));
    partial void OnScaleTextColorChanged(string value) => OnPropertyChanged(nameof(ScaleColor));
    partial void OnAccentColorChanged(string value) => OnPropertyChanged(nameof(Accent));
}

public static class VuMeterSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildConfigPath();

    public static VuMeterSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<VuMeterSettings>(json, Options);
                if (settings is not null) return settings;
            }
        }
        catch { }
        return new VuMeterSettings();
    }

    public static void Save(VuMeterSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }

    private static string BuildConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "vu-meter.json");
    }
}
