using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.Core.Playback;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel de la botonera (Modulo 3B).
///
/// FASE C: cada pestaña de Cart tiene su propio CartBoardPlayer y su
/// propio SimpleBusSelectorViewModel. El bus elegido por el usuario se
/// persiste en el .rcb (OutputBusId) y se restaura al abrir el archivo.
/// -------------
public sealed partial class CartBoardViewModel : ViewModelBase
{
    private readonly CartBoard _board;
    private readonly CartBoardPlayer? _player;

    /// -------------
    /// Func que devuelve el bus "del modulo Cart" (el default). Se usa
    /// como fallback cuando el .rcb no tiene OutputBusId o cuando el bus
    /// guardado ya no existe. Lo inyecta el MainWindowViewModel.
    /// -------------
    private readonly Func<IAudioBus?>? _defaultBusProvider;

    private string? _currentFilePath;
    private bool _suppressBusCallback;

    // ───── Coleccion de botones ──────────────────────────────────────────
    public ObservableCollection<CartButtonViewModel> Buttons { get; } = new();
    [ObservableProperty] private CartButtonViewModel? _selectedButton;

    // ───── Selector de bus de salida ─────────────────────────────────────
    /// -------------
    /// Selector de bus propio de esta botonera. Se inyecta desde el
    /// MainWindowViewModel via AttachBusSelector.
    /// -------------
    [ObservableProperty] private SimpleBusSelectorViewModel? _busSelector;

    // ───── Estado de la botonera ─────────────────────────────────────────
    [ObservableProperty] private string _boardName;
    [ObservableProperty] private int _rows = 5;
    [ObservableProperty] private int _columns = 10;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = "Botonera lista.";

    [ObservableProperty] private bool _isAnyPlaying;
    [ObservableProperty] private bool _isPausedByCoordinator;

    public int Capacity => Rows * Columns;
    public int AssignedCount => Buttons.Count(b => b.HasFile);
    public bool HasSelection => SelectedButton is not null;
    public bool HasNoSelection => !HasSelection;

    public string SummaryText =>
        $"{AssignedCount} de {Capacity} botones asignados";

    public string TitleWithDirty => IsDirty ? BoardName + " *" : BoardName;

    // ───── Constructor ───────────────────────────────────────────────────

    public CartBoardViewModel(CartBoardPlayer? player = null)
        : this(CartBoardStore.LoadOrCreateDefault(), CartBoardStore.DefaultPath, player, null)
    {
    }

    public CartBoardViewModel(
        CartBoard board,
        string? filePath,
        CartBoardPlayer? player,
        Func<IAudioBus?>? defaultBusProvider = null)
    {
        _board = board ?? new CartBoard();
        _board.Normalize();

        _player = player;
        _defaultBusProvider = defaultBusProvider;
        _currentFilePath = filePath;

        _boardName = _board.Name;
        _rows = _board.Rows;
        _columns = _board.Columns;
        _loadedFilePath = filePath;

        foreach (var btn in _board.Buttons)
        {
            var vm = new CartButtonViewModel(btn);
            vm.Changed += OnButtonChanged;
            Buttons.Add(vm);
        }

        if (_player is not null)
        {
            _player.ButtonStateChanged += OnPlayerStateChanged;
            _player.CrossfadeStateChanged += OnCrossfadeStateChanged;

            // Estado inicial: reflejar por si ya habia suspendidos al crear el VM.
            try
            {
                IsAnyPlaying = _player.HasAnyPlaying;
                IsPausedByCoordinator = _player.HasSuspendedChannels;
            }
            catch { }
        }
    }

    // ───── Attach del selector de bus ────────────────────────────────────

    /// -------------
    /// Conecta el selector de bus a esta botonera. Se llama desde el
    /// MainWindowViewModel despues de crear el VM.
    ///
    /// Suscribe el callback de cambio de bus y aplica el bus inicial
    /// (guardado en el .rcb o el del modulo Cart).
    /// -------------
    public void AttachBusSelector(SimpleBusSelectorViewModel selector)
    {
        if (selector is null) return;

        if (BusSelector is not null)
        {
            try { BusSelector.BusChanged -= OnBusSelectorChanged; } catch { }
        }

        BusSelector = selector;
        BusSelector.BusChanged += OnBusSelectorChanged;

        // Restaurar el bus guardado o caer al default.
        RestoreBusFromModel();
    }

    private void OnBusSelectorChanged(object? sender, IAudioBus bus)
    {
        if (_suppressBusCallback) return;

        _board.OutputBusId = bus.Id;
        IsDirty = true;

        ApplyBusToPlayer(bus);
        StatusMessage = $"Bus de salida: {bus.DisplayName}";
    }

    /// -------------
    /// Restaura el bus de salida a partir del modelo persistido:
    ///   1. Si OutputBusId tiene un bus valido, lo selecciona.
    ///   2. Si esta vacio o el bus ya no existe, cae al bus del modulo Cart.
    ///
    /// No marca la pestaña como dirty.
    /// -------------
    private void RestoreBusFromModel()
    {
        if (BusSelector is null) return;

        if (!string.IsNullOrEmpty(_board.OutputBusId) &&
            BusSelector.TrySelectBusById(_board.OutputBusId))
        {
            ApplyBusToPlayer(BusSelector.SelectedBus);
            return;
        }

        // Fallback: bus del modulo Cart (o el primario si no hay provider).
        var defaultBus = _defaultBusProvider?.Invoke();

        _suppressBusCallback = true;
        try
        {
            if (defaultBus is not null)
                BusSelector.SelectBusSilently(defaultBus);
            else
                BusSelector.RefreshBuses();
        }
        finally
        {
            _suppressBusCallback = false;
        }

        ApplyBusToPlayer(BusSelector.SelectedBus);
    }

    private void ApplyBusToPlayer(IAudioBus? bus)
    {
        try { _player?.SetBus(bus); } catch { }
    }

    // ───── Comandos ──────────────────────────────────────────────────────

    public void TriggerButton(CartButtonViewModel? buttonVm)
    {
        if (buttonVm is null) return;
        if (_player is null)
        {
            StatusMessage = "Sin motor de reproduccion.";
            return;
        }

        try
        {
            _player.TogglePlay(buttonVm.Source);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reproduciendo: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        try
        {
            _player?.StopAll();
            StatusMessage = "Todos los botones detenidos.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al detener: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearSelectedButton()
    {
        if (SelectedButton is null) return;

        SelectedButton.Clear();
        IsDirty = true;
        StatusMessage = "Boton limpiado.";
        RefreshSummary();
    }

    public void AssignFileToSelected(string filePath)
    {
        if (SelectedButton is null) return;
        AssignFileToButton(SelectedButton, filePath);
    }

    public void AssignFileToButton(CartButtonViewModel buttonVm, string filePath)
    {
        if (buttonVm is null) return;
        if (string.IsNullOrWhiteSpace(filePath)) return;

        bool shouldUpdateLabel = buttonVm.IsLabelAutoGenerated();

        buttonVm.FilePath = filePath;

        if (shouldUpdateLabel)
            buttonVm.Label = Path.GetFileNameWithoutExtension(filePath);

        IsDirty = true;
        StatusMessage = $"Asignado: {Path.GetFileName(filePath)}";
        RefreshSummary();
    }

    // ───── Apertura / guardado .rcb ─────────────────────────────────────

    public bool LoadFromFile(string filePath)
    {
        var loaded = CartBoardStore.Load(filePath);
        if (loaded is null)
        {
            StatusMessage = $"No se pudo cargar '{Path.GetFileName(filePath)}'.";
            return false;
        }

        ApplyLoadedBoard(loaded, filePath);
        return true;
    }

    public bool SaveToFile(string? filePath = null)
    {
        var target = filePath ?? _currentFilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = "No hay ruta de guardado.";
            return false;
        }

        try
        {
            SyncButtonsToModel();
            _board.Name = BoardName;
            _board.Rows = Rows;
            _board.Columns = Columns;

            // Persistir el bus actual del selector.
            if (BusSelector?.SelectedBus is IAudioBus selBus)
                _board.OutputBusId = selBus.Id;

            CartBoardStore.Save(_board, target);

            if (!target.EndsWith(CartBoardStore.FileExtension, StringComparison.OrdinalIgnoreCase))
                target += CartBoardStore.FileExtension;

            _currentFilePath = target;
            LoadedFilePath = target;
            IsDirty = false;

            StatusMessage = $"Guardado en '{Path.GetFileName(target)}'.";
            OnPropertyChanged(nameof(TitleWithDirty));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
            return false;
        }
    }

    public void NewBoard()
    {
        var fresh = new CartBoard();
        fresh.Normalize();

        ApplyLoadedBoard(fresh, null);
        StatusMessage = "Botonera nueva creada.";
    }

    private void ApplyLoadedBoard(CartBoard board, string? filePath)
    {
        foreach (var vm in Buttons)
        {
            vm.Changed -= OnButtonChanged;
            try { vm.Dispose(); } catch { }
        }
        Buttons.Clear();

        _board.Version = board.Version;
        _board.Name = board.Name;
        _board.Rows = board.Rows;
        _board.Columns = board.Columns;
        _board.Buttons = new List<CartButton>(board.Buttons);
        _board.OutputBusId = board.OutputBusId;
        _board.CreatedAtUtc = board.CreatedAtUtc;
        _board.ModifiedAtUtc = board.ModifiedAtUtc;

        BoardName = _board.Name;
        Rows = _board.Rows;
        Columns = _board.Columns;

        foreach (var btn in _board.Buttons)
        {
            var vm = new CartButtonViewModel(btn);
            vm.Changed += OnButtonChanged;
            Buttons.Add(vm);
        }

        _currentFilePath = filePath;
        LoadedFilePath = filePath;
        IsDirty = false;

        // Restaurar el bus del modelo recien cargado.
        if (BusSelector is not null)
            RestoreBusFromModel();

        RefreshSummary();
    }

    // ───── Handlers ──────────────────────────────────────────────────────

    private void OnButtonChanged(object? sender, EventArgs e)
    {
        if (sender is CartButtonViewModel button)
            _player?.SetLiveVolume(button.Source.Id, button.VolumeDb);

        IsDirty = true;
        RefreshSummary();
    }

    private void OnPlayerStateChanged(object? sender, CartButtonPlaybackEvent e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var vm in Buttons)
            {
                if (vm.Source.Id == e.ButtonId)
                    vm.IsPlaying = e.IsPlaying;
            }

            if (_player is not null)
            {
                IsAnyPlaying = _player.HasAnyPlaying;
                IsPausedByCoordinator = _player.HasSuspendedChannels;
            }
        });
    }

    private void OnCrossfadeStateChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            OnPropertyChanged(nameof(IsCrossfadeEnabled)));
    }

    // ───── Helpers ───────────────────────────────────────────────────────

    private void SyncButtonsToModel()
    {
        _board.Buttons.Clear();
        foreach (var vm in Buttons)
        {
            vm.ApplyToModel();
            _board.Buttons.Add(vm.Source);
        }
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(AssignedCount));
        OnPropertyChanged(nameof(Capacity));
        OnPropertyChanged(nameof(SummaryText));
    }

    // ───── Crossfade (existente) ────────────────────────────────────────

    public bool IsCrossfadeEnabled
    {
        get => _player?.IsCrossfadeEnabled ?? false;
        set
        {
            if (_player is null) return;
            if (_player.IsCrossfadeEnabled == value) return;

            _player.IsCrossfadeEnabled = value;
            OnPropertyChanged();

            StatusMessage = value
                ? $"Crossfade ACTIVADO ({_player.CrossfadeDurationMs} ms)"
                : "Crossfade DESACTIVADO (los carts se superponen)";
        }
    }

    [RelayCommand]
    private void ToggleCrossfade()
    {
        IsCrossfadeEnabled = !IsCrossfadeEnabled;
    }

    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedButton is null) { StatusMessage = "Selecciona un boton primero."; return; }
        TriggerButton(SelectedButton);
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnBoardNameChanged(string value)
    {
        _board.Name = value ?? string.Empty;
        OnPropertyChanged(nameof(TitleWithDirty));
        IsDirty = true;
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TitleWithDirty));

    partial void OnSelectedButtonChanged(CartButtonViewModel? oldValue, CartButtonViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;

        if (newValue is not null)
            newValue.IsSelected = true;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }
}
