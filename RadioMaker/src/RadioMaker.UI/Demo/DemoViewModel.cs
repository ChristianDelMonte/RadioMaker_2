using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using System;

namespace RadioMaker.UI.Demo;

/// -------------
/// ViewModel del overlay de presentación inicial. Se crea una sola vez
/// al arranque (si DemoCompleted == false) y se destruye al terminar.
/// -------------
public sealed partial class DemoViewModel : ObservableObject, IDisposable
{
    private readonly DemoService _service;
    private readonly DispatcherTimer _progressTimer;
    private bool _disposed;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public DemoViewModel(DemoService service)
    {
        _service = service;
        _service.AudioStarted += OnAudioStarted;
        _service.PlaybackCompleted += OnPlaybackCompleted;
        _service.Failed += OnFailed;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _progressTimer.Tick += OnProgressTick;

        // Si el service ya cargó audios, activar el overlay y arrancar.
        IsActive = _service.IsLoaded;
        TotalCount = _service.Audios.Count;

        if (IsActive)
        {
            _progressTimer.Start();
            _ = _service.PlayAsync();
        }
    }

    private void OnAudioStarted(DemoAudioEntry entry, int index, int total) =>
        Dispatcher.UIThread.Post(() =>
        {
            CurrentTitle = entry.DisplayTitle;
            CurrentIndex = index;
            TotalCount = total;
            ProgressFraction = 0;
            IsActive = true;
            IsPaused = false;
        });

    private void OnPlaybackCompleted() =>
        Dispatcher.UIThread.Post(Close);

    private void OnFailed(string error) =>
        Dispatcher.UIThread.Post(() =>
        {
            CurrentTitle = "Error en la presentación";
            ErrorMessage = error;
            HasError = true;
            // NO cerrar el overlay: el usuario debe ver el error.
            // Los botones siguen habilitados: puede clickear Saltar.
        });

    private void OnProgressTick(object? sender, EventArgs e)
    {
        try
        {
            if (!IsActive) return;

            var channel = _service.GetCurrentChannel();
            if (channel is null) return;

            var pos = channel.Position.TotalSeconds;
            var dur = channel.Duration.TotalSeconds;
            if (dur > 0.1)
                ProgressFraction = Math.Clamp(pos / dur, 0, 1);

            IsPaused = channel.State == PlaybackState.Paused;
        }
        catch { }
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (IsPaused)
        {
            _service.Resume();
            IsPaused = false;
        }
        else
        {
            _service.Pause();
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void Skip() => Close();

    /// -------------
    /// Devuelve el canal de audio del demo (o null si no está sonando).
    /// Se usa desde MainWindowViewModel para alimentar el VU meter y el
    /// Spectrum mientras la demo corre.
    /// -------------
    public RadioMaker.Core.Abstractions.IAudioChannel? GetCurrentChannel()
        => _service.GetCurrentChannel();

    private void Close()
    {
        _progressTimer.Stop();
        _service.Skip();
        IsActive = false;
        HasError = false;
        ErrorMessage = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _progressTimer.Stop();
        _service.AudioStarted -= OnAudioStarted;
        _service.PlaybackCompleted -= OnPlaybackCompleted;
        _service.Failed -= OnFailed;
    }
}