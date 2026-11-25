using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;
using RadioMaker.UI.Localization;
using System.Collections.ObjectModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del contenido de una pestaña Playlist.
///
/// Ademas de editar la playlist (agregar/quitar/mover pistas, guardar,
/// importar/exportar), permite reproducirla a traves del PlaylistPlayer
/// compartido de la aplicacion.
///
/// La reproduccion real se hace a traves de PlaylistPlayer, pero solo UNA
/// playlist puede sonar a la vez. Si esta pestaña no es la que esta sonando,
/// los botones de transporte reflejan el estado global (Play deshabilitado,
/// por ejemplo).
/// -------------
public sealed partial class PlaylistEditorViewModel : ViewModelBase
{
    private Playlist _playlist;
    private string? _currentFilePath;
    private readonly PlaylistPlayer? _player;

    /// -------------
    /// Func que devuelve el bus "del aire" (el del modulo Scheduler).
    /// Se usa como default al crear la pestaña y como fallback cuando el
    /// bus persistido en el .rmf ya no existe. Lo inyecta el TabHost.
    /// -------------
    private readonly Func<IAudioBus?>? _schedulerBusProvider;
    private Action? _stopPlaybackAction;
    private Func<bool>? _autoDjEnabledProvider;

    // ───── Estado ────────────────────────────────────────────────────────

    [ObservableProperty] private string _playlistName;
    [ObservableProperty] private PlaylistMode _mode;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.Initial");
    [ObservableProperty] private PlaylistTrackViewModel? _selectedTrack;

    /// -------------
    /// Selector de bus de salida de esta playlist. Es un SimpleBusSelector:
    /// cada pestaña tiene su propio bus, sin depender del routing global.
    /// -------------
    [ObservableProperty] private SimpleBusSelectorViewModel? _busSelector;

    /// -------------Estado de reproduccion de ESTA playlist (viene del player).</summary>
    [ObservableProperty] private PlaylistPlayerState _playerState = PlaylistPlayerState.Stopped;
    [ObservableProperty] private bool _isAutoDjPlaylist;
    public ObservableCollection<PlaylistTrackViewModel> Tracks { get; } = new();

    public PlaylistMode[] AvailableModes { get; } =
        { PlaylistMode.Once, PlaylistMode.Loop, PlaylistMode.Shuffle };

    // ───── Propiedades derivadas ─────────────────────────────────────────

    /// -------------
    /// Reproductor de esta pestaña. Se expone publicamente para que el
    /// MainWindowViewModel pueda consultar su estado y liberarlo al cerrar
    /// la pestaña, sin necesidad de reflection.
    /// -------------
    public PlaylistPlayer? Player => _player;

    public string TitleWithDirty => IsDirty ? PlaylistName + " *" : PlaylistName;

    public int TrackCount => Tracks.Count;
    public int EnabledCount => Tracks.Count(t => t.IsEnabled);
    public int MixCount => Tracks.Count(t => t.HasMix);

    public bool HasTracks => Tracks.Count > 0;
    public bool HasNoTracks => !HasTracks;
    public bool HasSelection => SelectedTrack is not null;
    public bool HasNoSelection => !HasSelection;

    public string SummaryText =>
        string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Summary"), TrackCount, EnabledCount, MixCount, ModeText);

    public string ModeText => Mode switch
    {
        PlaylistMode.Once => LocalizationService.GetString("Loc.PlaylistEditor.Mode.Once"),
        PlaylistMode.Loop => LocalizationService.GetString("Loc.PlaylistEditor.Mode.Loop"),
        PlaylistMode.Shuffle => LocalizationService.GetString("Loc.PlaylistEditor.Mode.Shuffle"),
        _ => Mode.ToString()
    };

    // ───── Totalizador de duracion ───────────────────────────────────────

    /// -------------
    /// Duracion total (en segundos) de las pistas HABILITADAS.
    /// Las pistas deshabilitadas no suman porque no se van a reproducir.
    /// -------------
    public double TotalDurationSeconds =>
        Tracks.Where(t => t.IsEnabled).Sum(t => t.DurationSeconds);

    /// -------------
    /// Cantidad de pistas habilitadas cuya duracion todavia no se analizo
    /// (DurationSeconds == 0 pero tienen un archivo asignado). Se usa para
    /// avisar al usuario que el total puede ser incompleto.
    /// -------------
    public int UnanalyzedEnabledCount =>
        Tracks.Count(t => t.IsEnabled
                          && t.DurationSeconds <= 0.5
                          && !string.IsNullOrWhiteSpace(t.FilePath));

    /// -------------
    /// Texto del total con formato HH:MM:SS.
    /// Si hay pistas habilitadas sin analizar, agrega " (+N sin analizar)"
    /// para no mentir con un numero incompleto.
    /// -------------
    public string TotalDurationText
    {
        get
        {
            var total = TimeSpan.FromSeconds(TotalDurationSeconds);
            var baseText = $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}";

            int unanalyzed = UnanalyzedEnabledCount;
            return unanalyzed > 0
                ? string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Duration.Unanalyzed"), baseText, unanalyzed)
                : baseText;
        }
    }

    // ───── Propiedades de transporte ─────────────────────────────────────

    /// -------------True si esta playlist en particular es la que esta sonando.</summary>
    public bool IsThisPlaylistPlaying =>
        _player is not null && (ReferenceEquals(_player.CurrentPlaylist, _playlist) ||
            (IsAutoDjPlaylist && (_autoDjEnabledProvider?.Invoke() ?? false)));

    public bool CanStartPlayback => !IsAutoDjPlaylist && HasTracks && !IsThisPlaylistPlaying;
    public bool ShowBusSelector => !IsAutoDjPlaylist;

    public bool CanPausePlayback =>
        IsThisPlaylistPlaying && PlayerState.Status == PlaylistPlayerStatus.Playing;

    public bool CanResumePlayback =>
        IsThisPlaylistPlaying &&
        (PlayerState.Status == PlaylistPlayerStatus.Paused ||
         PlayerState.Status == PlaylistPlayerStatus.PausedByScheduler);

    public bool CanStopPlayback => IsThisPlaylistPlaying &&
        (!IsAutoDjPlaylist || (_autoDjEnabledProvider?.Invoke() ?? PlayerState.Status != PlaylistPlayerStatus.Stopped));

    public bool CanNextPlayback => IsThisPlaylistPlaying && PlayerState.Status != PlaylistPlayerStatus.Stopped;
    public bool CanPrevPlayback => IsThisPlaylistPlaying && PlayerState.Status != PlaylistPlayerStatus.Stopped;

    public string PlayerStateText => IsAutoDjPlaylist &&
        (_autoDjEnabledProvider?.Invoke() ?? false) && PlayerState.Status == PlaylistPlayerStatus.Stopped
            ? LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.WaitingOnAir")
            : !IsThisPlaylistPlaying ? LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Stopped")
            : PlayerState.Status switch
            {
                PlaylistPlayerStatus.Stopped => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Stopped"),
                PlaylistPlayerStatus.Playing => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Playing"),
                PlaylistPlayerStatus.Paused => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Paused"),
                PlaylistPlayerStatus.PausedByScheduler => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.PausedByScheduler"),
                PlaylistPlayerStatus.Delaying => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Delaying"),
                PlaylistPlayerStatus.Error => LocalizationService.GetString("Loc.PlaylistEditor.PlayerState.Error"),
                _ => "?"
            };

    public string PlayerStateColor => !IsThisPlaylistPlaying
        ? "#6B7280"
        : PlayerState.StatusColor;

    public bool HasPlayerTrack => IsThisPlaylistPlaying && PlayerState.HasTrack;

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaylistEditorViewModel()
        : this(new Playlist { Name = LocalizationService.GetString("Loc.PlaylistEditor.NewPlaylist") }, null)
    {
    }

    public PlaylistEditorViewModel(
        Playlist playlist,
        PlaylistPlayer? player,
        Func<IAudioBus?>? schedulerBusProvider = null,
        Action? stopPlaybackAction = null)
    {
        _playlist = playlist ?? new Playlist();
        _playlistName = _playlist.Name;
        _notes = _playlist.Notes;
        _mode = _playlist.Mode;
        _player = player;
        _schedulerBusProvider = schedulerBusProvider;
        _stopPlaybackAction = stopPlaybackAction;

        RebuildTracksFromModel();

        if (_player is not null)
        {
            _player.StateChanged += OnPlayerStateChanged;
            _player.SessionStarted += OnPlayerSessionStarted;
            _player.SessionEnded += OnPlayerSessionEnded;
            _player.TrackCompleted += OnPlayerTrackCompleted;
        }
    }

    private void RebuildTracksFromModel()
    {
        foreach (var vm in Tracks)
        {
            vm.Changed -= OnTrackChanged;
            try { vm.Dispose(); } catch { }
        }
        Tracks.Clear();

        foreach (var track in _playlist.Tracks)
        {
            var vm = new PlaylistTrackViewModel(track);
            vm.Changed += OnTrackChanged;
            Tracks.Add(vm);
        }

        SelectedTrack = Tracks.FirstOrDefault();
        RefreshSummary();
        RefreshTransportState();
    }

    // ───── Comandos de transporte ────────────────────────────────────────

    [RelayCommand]
    private void StartPlayback()
    {
        if (IsAutoDjPlaylist) return;
        if (_player is null) return;
        if (!HasTracks) return;

        // Determinar el indice de inicio: la pista seleccionada, o la primera.
        int startIndex = 0;
        if (SelectedTrack is not null)
        {
            int idx = Tracks.IndexOf(SelectedTrack);
            if (idx >= 0) startIndex = idx;
        }

        try
        {
            // Sincronizar cambios pendientes al modelo antes de reproducir.
            SyncTracksToModel();
            _player.Play(_playlist, startIndex);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.PlayError"), ex.Message);
        }
    }

    [RelayCommand]
    private void PausePlayback()
    {
        if (_player is null) return;
        try { _player.Pause(); } catch { }
    }

    [RelayCommand]
    private void ResumePlayback()
    {
        if (_player is null) return;
        try { _player.Resume(); } catch { }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        if (_player is null) return;
        if (IsAutoDjPlaylist && _stopPlaybackAction is not null)
        {
            _stopPlaybackAction();
            return;
        }
        try { _player.Stop(); } catch { }
    }

    public void SetStopPlaybackAction(Action action) => _stopPlaybackAction = action;
    public void SetAutoDjEnabledProvider(Func<bool> provider)
    {
        _autoDjEnabledProvider = provider;
        RefreshTransportState();
        OnPropertyChanged(nameof(PlayerStateText));
    }

    public void RefreshAutoDjState()
    {
        RefreshTransportState();
        OnPropertyChanged(nameof(PlayerStateText));
    }

    [RelayCommand]
    private void NextPlayback()
    {
        if (_player is null) return;
        try { _player.Next(); } catch { }
    }

    [RelayCommand]
    private void PrevPlayback()
    {
        if (_player is null) return;
        try { _player.Prev(); } catch { }
    }

    // ───── Handler del player ────────────────────────────────────────────

    private void OnPlayerStateChanged(object? sender, PlaylistPlayerState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_player is not null && ReferenceEquals(_player.CurrentPlaylist, _playlist))
            {
                PlayerState = state;

                // Actualizar el flag IsCurrentlyPlaying de cada pista.
                int activeIndex = -1;
                if (state.Status == PlaylistPlayerStatus.Playing ||
                    state.Status == PlaylistPlayerStatus.Paused ||
                    state.Status == PlaylistPlayerStatus.PausedByScheduler ||
                    state.Status == PlaylistPlayerStatus.Delaying)
                {
                    activeIndex = state.TrackIndex;
                }

                for (int i = 0; i < Tracks.Count; i++)
                    Tracks[i].IsCurrentlyPlaying = (i == activeIndex);
            }
            else
            {
                PlayerState = PlaylistPlayerState.Stopped;

                // Limpiar todos los flags de "reproduciendo".
                foreach (var t in Tracks)
                    t.IsCurrentlyPlaying = false;
            }

            RefreshTransportState();
        });
    }

    private void OnPlayerSessionStarted(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Solo resetear si la sesion es para NUESTRA playlist.
            if (_player is null) return;
            if (!ReferenceEquals(_player.CurrentPlaylist, _playlist)) return;

            foreach (var track in Tracks)
                track.IsPlayed = false;
        });
    }

    private void OnPlayerSessionEnded(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var track in Tracks)
            {
                track.IsPlayed = false;
                track.IsCurrentlyPlaying = false;
            }
        });
    }

    private void OnPlayerTrackCompleted(object? sender, int trackIndex)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_player is null) return;
            if (!ReferenceEquals(_player.CurrentPlaylist, _playlist)) return;

            if (trackIndex < 0 || trackIndex >= Tracks.Count) return;

            Tracks[trackIndex].IsPlayed = true;
        });
    }

    private void RefreshTransportState()
    {
        OnPropertyChanged(nameof(IsThisPlaylistPlaying));
        OnPropertyChanged(nameof(CanStartPlayback));
        OnPropertyChanged(nameof(CanPausePlayback));
        OnPropertyChanged(nameof(CanResumePlayback));
        OnPropertyChanged(nameof(CanStopPlayback));
        OnPropertyChanged(nameof(CanNextPlayback));
        OnPropertyChanged(nameof(CanPrevPlayback));
        OnPropertyChanged(nameof(PlayerStateText));
        OnPropertyChanged(nameof(PlayerStateColor));
        OnPropertyChanged(nameof(HasPlayerTrack));
    }

    // ───── Comandos de pistas ────────────────────────────────────────────

    [RelayCommand]
    private void AddTrack()
    {
        var track = new PlaylistTrack { IsEnabled = true };
        var vm = new PlaylistTrackViewModel(track);
        vm.Changed += OnTrackChanged;

        Tracks.Add(vm);
        _playlist.Tracks.Add(track);

        SelectedTrack = vm;
        IsDirty = true;
        StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.TrackAdded");
        RefreshSummary();
        RefreshTransportState();
    }

    [RelayCommand]
    private void RemoveTrack(PlaylistTrackViewModel? trackVm)
    {
        trackVm ??= SelectedTrack;
        if (trackVm is null) return;
        if (!Tracks.Contains(trackVm)) return;

        trackVm.Changed -= OnTrackChanged;
        try { trackVm.Dispose(); } catch { }
        Tracks.Remove(trackVm);
        _playlist.Tracks.Remove(trackVm.Source);

        if (SelectedTrack == trackVm)
            SelectedTrack = Tracks.FirstOrDefault();

        IsDirty = true;
        StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.TrackRemoved");
        RefreshSummary();
        RefreshTransportState();
    }

    [RelayCommand]
    private void MoveTrackUp(PlaylistTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = Tracks.IndexOf(trackVm);
        if (idx <= 0) return;

        Tracks.Move(idx, idx - 1);
        SyncTrackOrderToModel();
        IsDirty = true;
    }

    [RelayCommand]
    private void MoveTrackDown(PlaylistTrackViewModel? trackVm)
    {
        if (trackVm is null) return;
        int idx = Tracks.IndexOf(trackVm);
        if (idx < 0 || idx >= Tracks.Count - 1) return;

        Tracks.Move(idx, idx + 1);
        SyncTrackOrderToModel();
        IsDirty = true;
    }

    [RelayCommand]
    private void ClearMix()
    {
        if (SelectedTrack is null) return;

        SelectedTrack.MixAudioPath = string.Empty;
        StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.MixRemoved");
    }

    public PlaylistTrackViewModel AddTrackFromFile(string filePath)
    {
        var track = new PlaylistTrack
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            IsEnabled = true
        };

        var vm = new PlaylistTrackViewModel(track);
        vm.Changed += OnTrackChanged;

        Tracks.Add(vm);
        _playlist.Tracks.Add(track);

        IsDirty = true;
        RefreshSummary();
        RefreshTransportState();
        return vm;
    }

    /// -------------
    /// Reemplaza las pistas actuales por las generadas automaticamente.
    /// Se usa desde el modal del generador (boton Aceptar).
    ///
    /// - Limpia todas las pistas actuales y sus suscripciones.
    /// - Agrega nuevas pistas SIN mixado (el usuario las edita despues).
    /// - Marca la playlist como dirty.
    /// -------------
    public void ReplaceTracksFromGenerator(IReadOnlyList<string> filePaths)
    {
        if (filePaths is null || filePaths.Count == 0) return;

        // Desenganchar y limpiar las pistas actuales.
        foreach (var vm in Tracks)
        {
            vm.Changed -= OnTrackChanged;
            try { vm.Dispose(); } catch { }
        }
        Tracks.Clear();
        _playlist.Tracks.Clear();

        // Agregar las nuevas.
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!File.Exists(path)) continue;

            var track = new PlaylistTrack
            {
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path),
                IsEnabled = true
                // Sin mixado: el generador arma pistas limpias.
            };

            var vm = new PlaylistTrackViewModel(track);
            vm.Changed += OnTrackChanged;
            Tracks.Add(vm);
            _playlist.Tracks.Add(track);
        }

        SelectedTrack = Tracks.FirstOrDefault();
        IsDirty = true;
        StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Generated"), Tracks.Count);
        RefreshSummary();
        RefreshTransportState();
    }

    /// <summary>Displays a generated AutoDJ preview on its dedicated, non-routable tab.</summary>
    public void SetAutoDjPlaylist(Playlist playlist, string statusMessage)
    {
        if (playlist is null) throw new ArgumentNullException(nameof(playlist));
        if (_player?.IsPlayingNow == true && ReferenceEquals(_player.CurrentPlaylist, _playlist)) return;

        _playlist = playlist;
        IsAutoDjPlaylist = true;
        _currentFilePath = null;
        LoadedFilePath = null;
        PlaylistName = "AUTODJ";
        Notes = string.Empty;
        Mode = playlist.Mode;
        RebuildTracksFromModel();
        IsDirty = false;
        StatusMessage = statusMessage switch
        {
            "AutoDJ listo para salir al aire." => LocalizationService.GetString("Loc.PlaylistEditor.Status.AutoDjReady"),
            _ when statusMessage.StartsWith("Vista previa AutoDJ guardada temporalmente (", StringComparison.Ordinal) =>
                string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.AutoDjSaved"),
                    System.Text.RegularExpressions.Regex.Match(statusMessage, @"\d+").Value),
            _ when statusMessage.StartsWith("Vista previa AutoDJ lista (", StringComparison.Ordinal) =>
                string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.AutoDjSaveError"),
                    System.Text.RegularExpressions.Regex.Match(statusMessage, @"\d+").Value,
                    statusMessage[(statusMessage.IndexOf(": ", StringComparison.Ordinal) + 2)..]),
            _ => statusMessage
        };
        OnPropertyChanged(nameof(ShowBusSelector));
        RefreshTransportState();
    }

    public void RefreshAutoDjStatus(string resourceKey, params object?[] args)
    {
        if (!IsAutoDjPlaylist) return;
        StatusMessage = string.Format(LocalizationService.GetString(resourceKey), args);
    }

    // ───── Apertura / guardado .rmf ──────────────────────────────────────

    public bool LoadFromFile(string filePath)
    {
        var loaded = PlaylistStore.Load(filePath);
        if (loaded is null)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.LoadFailed"), Path.GetFileName(filePath));
            return false;
        }

        // Si estaba reproduciendo ESTA playlist, detener.
        if (_player is not null && ReferenceEquals(_player.CurrentPlaylist, _playlist))
            _player.Stop();

        _playlist = loaded;
        PlaylistName = loaded.Name;
        Notes = loaded.Notes;
        Mode = loaded.Mode;
        RebuildTracksFromModel();

        _currentFilePath = filePath;
        LoadedFilePath = filePath;
        IsDirty = false;

        // ── Restaurar el bus elegido por el usuario ─────────────────────
        RestoreBusFromModel();

        StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Loaded"), Path.GetFileName(filePath), Tracks.Count);
        return true;
    }

    /// -------------
    /// Restaura el bus de salida a partir del modelo persistido:
    ///   1. Si OutputBusId tiene un bus valido, lo selecciona.
    ///   2. Si esta vacio o el bus ya no existe, cae al bus del Scheduler.
    ///
    /// No marca la pestaña como dirty (usa seleccion silenciosa).
    /// -------------
    private void RestoreBusFromModel()
    {
        if (BusSelector is null) return;

        // 1) Intentar el bus guardado en el .rmf.
        if (!string.IsNullOrEmpty(_playlist.OutputBusId))
        {
            if (BusSelector.TrySelectBusById(_playlist.OutputBusId))
            {
                ApplyBusToPlayer(BusSelector.SelectedBus);
                return;
            }

            // No existe: se cae al default con aviso.
            StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.BusMissing");
        }

        // 2) Fallback al bus del Scheduler.
        var schedulerBus = _schedulerBusProvider?.Invoke();
        if (schedulerBus is not null)
        {
            BusSelector.SelectBusSilently(schedulerBus);
        }
        else
        {
            // Sin provider: al menos garantizamos que haya una seleccion.
            BusSelector.RefreshBuses();
        }

        ApplyBusToPlayer(BusSelector.SelectedBus);
    }

    /// -------------
    /// Conecta el selector de bus. Lo llama el TabHost despues de crear
    /// el editor, para que el editor pueda escuchar los cambios del bus
    /// y persistirlos en el modelo.
    /// -------------
    public void AttachBusSelector(SimpleBusSelectorViewModel selector)
    {
        if (selector is null) return;

        // Desuscribir el anterior (si lo habia).
        if (BusSelector is not null)
        {
            try { BusSelector.BusChanged -= OnBusSelectorChanged; } catch { }
        }

        BusSelector = selector;
        BusSelector.BusChanged += OnBusSelectorChanged;

        // Aplicar el bus al player inmediatamente (por si arranca sin ser cargado).
        ApplyBusToPlayer(selector.SelectedBus);
    }

    private void OnBusSelectorChanged(object? sender, IAudioBus bus)
    {
        // Persistir en el modelo.
        _playlist.OutputBusId = bus.Id;
        IsDirty = true;

        // Aplicar al player.
        ApplyBusToPlayer(bus);

        StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.OutputBus"), bus.DisplayName);
    }

    /// -------------
    /// Aplica el bus al player de esta pestaña.
    /// -------------
    private void ApplyBusToPlayer(IAudioBus? bus)
    {
        try { _player?.SetBus(bus); } catch { }
    }

    public bool SaveToFile(string? filePath = null)
    {
        var target = filePath ?? _currentFilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.NoSavePath");
            return false;
        }

        try
        {
            SyncTracksToModel();
            _playlist.Name = PlaylistName ?? string.Empty;
            _playlist.Notes = Notes ?? string.Empty;
            _playlist.Mode = Mode;

            PlaylistStore.Save(_playlist, target);

            if (!target.EndsWith(PlaylistStore.FileExtension, StringComparison.OrdinalIgnoreCase))
                target += PlaylistStore.FileExtension;

            _currentFilePath = target;
            LoadedFilePath = target;
            IsDirty = false;

            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Saved"), Path.GetFileName(target));
            OnPropertyChanged(nameof(TitleWithDirty));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.SaveError"), ex.Message);
            return false;
        }
    }

    public void NewPlaylist()
    {
        if (_player is not null && ReferenceEquals(_player.CurrentPlaylist, _playlist))
            _player.Stop();

        _playlist = new Playlist { Name = LocalizationService.GetString("Loc.PlaylistEditor.NewPlaylist") };
        PlaylistName = _playlist.Name;
        Notes = string.Empty;
        Mode = PlaylistMode.Once;
        RebuildTracksFromModel();

        _currentFilePath = null;
        LoadedFilePath = null;
        IsDirty = false;

        // Resetear el bus al default del Scheduler.
        var schedulerBus = _schedulerBusProvider?.Invoke();
        if (schedulerBus is not null && BusSelector is not null)
            BusSelector.SelectBusSilently(schedulerBus);
        ApplyBusToPlayer(BusSelector?.SelectedBus);

        StatusMessage = LocalizationService.GetString("Loc.PlaylistEditor.Status.NewPlaylist");
    }

    // ───── Importar / Exportar M3U / PLS ────────────────────────────────

    public bool ImportFromExternal(string filePath)
    {
        var imported = PlaylistFormatIO.ImportAuto(filePath);
        if (imported is null || imported.Tracks.Count == 0)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.ImportFailed"), Path.GetFileName(filePath));
            return false;
        }

        if (_player is not null && ReferenceEquals(_player.CurrentPlaylist, _playlist))
            _player.Stop();

        _playlist = imported;
        _playlist.Name = Path.GetFileNameWithoutExtension(filePath);

        PlaylistName = _playlist.Name;
        Notes = _playlist.Notes;
        Mode = _playlist.Mode;
        RebuildTracksFromModel();

        _currentFilePath = null;
        LoadedFilePath = null;
        IsDirty = true;

        // Resetear el bus al default (un .m3u/.pls no trae config de bus).
        var schedulerBus = _schedulerBusProvider?.Invoke();
        if (schedulerBus is not null && BusSelector is not null)
            BusSelector.SelectBusSilently(schedulerBus);
        ApplyBusToPlayer(BusSelector?.SelectedBus);

        StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Imported"), Tracks.Count, Path.GetFileName(filePath));
        return true;
    }

    public bool ExportToM3U(string filePath)
    {
        try
        {
            SyncTracksToModel();
            PlaylistFormatIO.ExportM3U(_playlist, filePath);
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Exported"), Path.GetFileName(filePath));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.ExportM3uError"), ex.Message);
            return false;
        }
    }

    public bool ExportToPLS(string filePath)
    {
        try
        {
            SyncTracksToModel();
            PlaylistFormatIO.ExportPLS(_playlist, filePath);
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.Exported"), Path.GetFileName(filePath));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Loc.PlaylistEditor.Status.ExportPlsError"), ex.Message);
            return false;
        }
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    private void SyncTracksToModel()
    {
        _playlist.Tracks.Clear();
        foreach (var vm in Tracks)
        {
            vm.ApplyToModel();
            _playlist.Tracks.Add(vm.Source);
        }
    }

    private void SyncTrackOrderToModel() => SyncTracksToModel();

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnPlaylistNameChanged(string value)
    {
        _playlist.Name = value ?? string.Empty;
        OnPropertyChanged(nameof(TitleWithDirty));
        IsDirty = true;
    }

    partial void OnNotesChanged(string value)
    {
        _playlist.Notes = value ?? string.Empty;
        IsDirty = true;
    }

    partial void OnModeChanged(PlaylistMode value)
    {
        _playlist.Mode = value;
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(SummaryText));
        IsDirty = true;
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TitleWithDirty));

    partial void OnSelectedTrackChanged(PlaylistTrackViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(CanStartPlayback));
    }

    partial void OnIsAutoDjPlaylistChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBusSelector));
        OnPropertyChanged(nameof(CanStartPlayback));
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        IsDirty = true;
        RefreshSummary();
        RefreshTransportState();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(MixCount));
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(HasNoTracks));
        OnPropertyChanged(nameof(SummaryText));

        // Totalizador de duracion (pistas habilitadas).
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(UnanalyzedEnabledCount));
        OnPropertyChanged(nameof(TotalDurationText));
    }
}
