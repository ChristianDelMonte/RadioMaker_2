using System.Globalization;
using RMVoice.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RMVoice.Core.Models;

/// -------------
/// Una región dentro de un archivo de audio (p. ej. "la hora 07", "el minuto 42",
/// "22 grados" o "65 por ciento").
/// -------------
public sealed class AudioRegion : ObservableObject
{
    private int _index;
    private double _startSeconds;
    private double _endSeconds;

    /// -------------
    /// Índice lógico de la región. Puede ser negativo (temperaturas bajo cero).
    /// -------------
    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set
        {
            if (SetProperty(ref _startSeconds, value))
            {
                OnPropertyChanged(nameof(StartText));
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public double EndSeconds
    {
        get => _endSeconds;
        set
        {
            if (SetProperty(ref _endSeconds, value))
            {
                OnPropertyChanged(nameof(EndText));
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public string StartText
    {
        get => TimeFormat.ToTimeString(_startSeconds);
        set
        {
            if (TimeFormat.TryParse(value, out var parsed))
                StartSeconds = parsed;
            else
                OnPropertyChanged(nameof(StartText));
        }
    }

    public string EndText
    {
        get => TimeFormat.ToTimeString(_endSeconds);
        set
        {
            if (TimeFormat.TryParse(value, out var parsed))
                EndSeconds = parsed;
            else
                OnPropertyChanged(nameof(EndText));
        }
    }

    public double DurationSeconds => Math.Max(0, _endSeconds - _startSeconds);

    public string DurationText => DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    public AudioRegion Clone() => new()
    {
        Index = Index,
        StartSeconds = StartSeconds,
        EndSeconds = EndSeconds
    };

    public override string ToString() => $"{Index:00}  {StartText} → {EndText}";
}
