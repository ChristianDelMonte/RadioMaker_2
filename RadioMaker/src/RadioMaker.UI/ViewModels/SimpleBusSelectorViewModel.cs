using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel de un selector de bus "simple" para usar dentro de pestañas
/// independientes (por ejemplo, las carpetas favoritas del panel derecho,
/// o las pestañas de Playlist).
///
/// A diferencia de <see cref="ModuleBusSelectorViewModel"/>, este selector:
///   - NO depende del <c>ModuleRoutingService</c>.
///   - NO respeta el modo "bus global" del panel de configuracion.
///   - Siempre permite cambiar de bus (CanChangeBus = true).
///
/// Su proposito es que cada pestaña pueda elegir su propia salida de audio
/// sin afectar la reproduccion de otros modulos ni de otras pestañas.
/// </summary>
public sealed partial class SimpleBusSelectorViewModel : ViewModelBase
{
    private readonly IAudioEngine _engine;
    private bool _suppressCallback;

    public ObservableCollection<IAudioBus> AvailableBuses { get; } = new();

    [ObservableProperty] private IAudioBus? _selectedBus;

    /// <summary>Siempre true: las pestañas no siguen el modo global.</summary>
    public bool CanChangeBus => true;

    /// <summary>
    /// Callback opcional que se dispara cuando el usuario cambia el bus.
    /// Recibe el bus seleccionado (nunca null).
    /// </summary>
    private readonly Action<IAudioBus>? _onBusChanged;

    /// <summary>
    /// Evento que se dispara cuando cambia el bus. Es equivalente al
    /// callback pero como evento, para poder suscribirse sin tener que
    /// pasarlo por el constructor.
    /// </summary>
    public event EventHandler<IAudioBus>? BusChanged;

    public SimpleBusSelectorViewModel(
        IAudioEngine engine,
        IAudioBus? initialBus = null,
        Action<IAudioBus>? onBusChanged = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _onBusChanged = onBusChanged;

        // Suprimir el callback durante la inicializacion. Evita pisar el
        // bus persistido con el principal cuando todavia no se asigno.
        _suppressCallback = true;
        try
        {
            RefreshBuses();

            var target = initialBus ?? AvailableBuses.FirstOrDefault();
            if (target is not null)
                SelectedBus = target;
        }
        finally
        {
            _suppressCallback = false;
        }
    }

    partial void OnSelectedBusChanged(IAudioBus? value)
    {
        if (_suppressCallback) return;
        if (value is null) return;

        try { _onBusChanged?.Invoke(value); } catch { }
        try { BusChanged?.Invoke(this, value); } catch { }
    }

    /// <summary>
    /// Re-consulta la lista de buses del engine.
    /// </summary>
    public void RefreshBuses()
    {
        var currentId = SelectedBus?.Id;

        AvailableBuses.Clear();
        foreach (var b in _engine.Buses)
            AvailableBuses.Add(b);

        // Preservar la seleccion por Id si aun existe.
        if (!string.IsNullOrEmpty(currentId))
        {
            var match = AvailableBuses.FirstOrDefault(b => b.Id == currentId);
            if (match is not null)
                SelectedBus = match;
        }

        // Si no hay seleccion, caer al primario.
        if (SelectedBus is null && AvailableBuses.Count > 0)
            SelectedBus = AvailableBuses[0];
    }

    /// <summary>
    /// Intenta seleccionar el bus por Id. Devuelve true si lo encontro y
    /// lo selecciono, false si el bus no existe (en cuyo caso NO cambia
    /// la seleccion actual).
    ///
    /// Se usa al abrir un archivo persistido para restaurar el bus elegido
    /// por el usuario, o para caer al default si el bus ya no existe.
    /// </summary>
    public bool TrySelectBusById(string? busId)
    {
        if (string.IsNullOrWhiteSpace(busId)) return false;

        var match = AvailableBuses.FirstOrDefault(b => b.Id == busId);
        if (match is null) return false;

        // Bypass del callback: no queremos marcar dirty al restaurar.
        _suppressCallback = true;
        try { SelectedBus = match; }
        finally { _suppressCallback = false; }

        return true;
    }

    /// <summary>
    /// Selecciona un bus SIN disparar callbacks ni eventos. Util para
    /// restaurar el bus al cargar un archivo sin marcar la pestaña como
    /// dirty.
    /// </summary>
    public void SelectBusSilently(IAudioBus? bus)
    {
        if (bus is null) return;

        _suppressCallback = true;
        try { SelectedBus = bus; }
        finally { _suppressCallback = false; }
    }
}