using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RMWave.Core.Models;

public sealed class Project
{
    public int SampleRate { get; set; } = 44100;
    public List<ProjectTrack> Tracks { get; set; } = new();
    public string Name { get; set; } = "";
    public string Episode { get; set; } = "";
    public string Author { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime ModifiedDate { get; set; } = DateTime.Now;
    public List<TimelineMarker> Markers { get; set; } = new();

    [JsonIgnore]
    public double Duration =>
        Tracks.Count == 0
            ? 0
            : Tracks.Max(t => t.Duration);
}

public sealed class ProjectTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Pista";
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; } = 0.0f;

    // Ecualizador de 3 bandas (dB, rango -12 a +12)
    public float EqBass { get; set; } = 0;
    public float EqMid { get; set; } = 0;
    public float EqTreble { get; set; } = 0;
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public int RowCount { get; set; } = 2;
    public List<AudioClip> Clips { get; set; } = new();
    public double Zoom { get; set; } = 60;

    // NUEVO: Loop independiente por pista.
    public bool LoopEnabled { get; set; } = false;
    public double LoopStart { get; set; } = 0;
    public double LoopEnd { get; set; } = 0;

    [JsonIgnore]
    public double Duration =>
        Clips.Count == 0
            ? 0
            : Clips.Max(c => c.EndTime);
}

public sealed class TimelineMarker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public double Time { get; set; } = 0;
}

public sealed class AudioClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = "Clip";
    public double StartTime { get; set; }
    public double SourceOffset { get; set; }
    public double Duration { get; set; }
    public double SourceFileDuration { get; set; }
    public float Gain { get; set; } = 1.0f;
    public int Row { get; set; } = 0;

    [JsonIgnore]
    public double EndTime => StartTime + Duration;

    public double FadeIn { get; set; } = 0;
    public double FadeOut { get; set; } = 0;
}