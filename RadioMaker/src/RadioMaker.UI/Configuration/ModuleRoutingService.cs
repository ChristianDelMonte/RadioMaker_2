using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.Configuration;

/// -------------
/// Modulo de la aplicacion que puede tener su propio bus de salida.
/// El FileManager esta reservado para futura funcionalidad de preescucha.
/// -------------
public enum AudioModuleKind
{
    Cart = 0,
    Playlist = 1,
    Scheduler = 2,
    FileManager = 3
}

/// -------------
/// Estado persistente del routing de buses por modulo.
/// Se guarda en %APPDATA%/RmsAudioPlayer/module-routing.json.
/// -------------
public sealed class ModuleRoutingState
{
    public int Version { get; set; } = 1;
    public bool UseGlobalBus { get; set; } = true;
    public string GlobalBusId { get; set; } = "primary";
    public string CartBusId { get; set; } = "primary";
    public string PlaylistBusId { get; set; } = "primary";
    public string SchedulerBusId { get; set; } = "primary";
    public string FileManagerBusId { get; set; } = "primary";

    /// -------------
    /// ID del bus que se considera "AIRE" para el indicador visual.
    /// Vacio = default (el bus efectivo del Scheduler).
    /// -------------
    public string AirBusId { get; set; } = string.Empty;
}

/// -------------
/// Servicio que decide que bus de salida usa cada modulo de la aplicacion.
///
/// Modelo:
///   - Si UseGlobalBus = true -> TODOS los modulos usan GlobalBus.
///   - Si UseGlobalBus = false -> cada modulo usa su propio bus.
///
/// Los IDs se persisten. Si un bus referenciado ya no existe (fue eliminado
/// desde la ventana de buses), GetEffectiveBus cae al GlobalBus.
/// -------------
public sealed class ModuleRoutingService
{
    private readonly IAudioEngine _engine;
    private readonly Dictionary<AudioModuleKind, string> _busIds = new();

    private bool _useGlobalBus;
    private IAudioBus _globalBus;
    private string _airBusId;

    /// -------------Cambio general: global o toggle de UseGlobalBus.</summary>
    public event EventHandler? Changed;

    /// -------------Cambio especifico del bus de un modulo.</summary>
    public event EventHandler<AudioModuleKind>? ModuleBusChanged;

    /// -------------Se agrego o quito un bus de la lista del engine.</summary>
    public event EventHandler? BusesListChanged;

    public bool UseGlobalBus => _useGlobalBus;
    public IAudioBus GlobalBus => _globalBus;
    public IReadOnlyList<IAudioBus> AvailableBuses => _engine.Buses;

    public ModuleRoutingService(IAudioEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        var state = ModuleRoutingStateStore.Load() ?? new ModuleRoutingState();

        _useGlobalBus = state.UseGlobalBus;
        _globalBus = ResolveOrFallback(state.GlobalBusId, engine.PrimaryBus);

        _busIds[AudioModuleKind.Cart] = state.CartBusId;
        _busIds[AudioModuleKind.Playlist] = state.PlaylistBusId;
        _busIds[AudioModuleKind.Scheduler] = state.SchedulerBusId;
        _busIds[AudioModuleKind.FileManager] = state.FileManagerBusId;

        _airBusId = state.AirBusId ?? string.Empty;
    }

    /// -------------
    /// Bus efectivo para un modulo. Si UseGlobalBus esta activo, devuelve el
    /// global. Si no, resuelve el ID guardado del modulo. Si el ID no existe
    /// (bus eliminado), cae al global.
    /// -------------
    public IAudioBus GetEffectiveBus(AudioModuleKind kind)
    {
        if (_useGlobalBus) return _globalBus;

        if (!_busIds.TryGetValue(kind, out var id) || string.IsNullOrEmpty(id))
            return _globalBus;

        return _engine.GetBus(id) ?? _globalBus;
    }

    /// -------------
    /// ID configurado para un modulo, independientemente de si existe.
    /// Util para inicializar el ComboBox sin que se "corrija" solo.
    /// -------------
    public string GetConfiguredBusId(AudioModuleKind kind)
    {
        if (_useGlobalBus) return _globalBus.Id;
        return _busIds.TryGetValue(kind, out var id) ? id : _globalBus.Id;
    }

    /// -------------Cambia el bus de un modulo. Ignorado si UseGlobalBus esta activo.</summary>
    public void SetModuleBus(AudioModuleKind kind, IAudioBus? bus)
    {
        if (bus is null) return;
        if (_useGlobalBus) return;

        var current = GetConfiguredBusId(kind);
        if (current == bus.Id) return;

        _busIds[kind] = bus.Id;
        Save();
        ModuleBusChanged?.Invoke(this, kind);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetUseGlobalBus(bool value)
    {
        if (_useGlobalBus == value) return;
        _useGlobalBus = value;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetGlobalBus(IAudioBus? bus)
    {
        if (bus is null) return;
        if (ReferenceEquals(_globalBus, bus)) return;

        _globalBus = bus;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// -------------
    /// ID del bus que se considera "AIRE" para el indicador visual.
    /// Si el valor persistido esta vacio, cae al bus efectivo del Scheduler.
    /// -------------
    public string AirBusId
    {
        get
        {
            if (!string.IsNullOrEmpty(_airBusId)) return _airBusId;
            try { return GetEffectiveBus(AudioModuleKind.Scheduler).Id; }
            catch { return "primary"; }
        }
        set
        {
            var v = value ?? string.Empty;
            if (_airBusId == v) return;
            _airBusId = v;
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// -------------
    /// Notifica que la lista de buses disponibles cambio (agregado / quitado).
    /// Los selectores de modulo refrescan sus combos.
    /// -------------
    public void NotifyBusesChanged()
    {
        BusesListChanged?.Invoke(this, EventArgs.Empty);
    }

    private IAudioBus ResolveOrFallback(string id, IAudioBus fallback)
    {
        if (string.IsNullOrEmpty(id)) return fallback;
        return _engine.GetBus(id) ?? fallback;
    }

    private void Save()
    {
        var state = new ModuleRoutingState
        {
            UseGlobalBus = _useGlobalBus,
            GlobalBusId = _globalBus.Id,
            CartBusId = _busIds.TryGetValue(AudioModuleKind.Cart, out var c) ? c : "primary",
            PlaylistBusId = _busIds.TryGetValue(AudioModuleKind.Playlist, out var p) ? p : "primary",
            SchedulerBusId = _busIds.TryGetValue(AudioModuleKind.Scheduler, out var s) ? s : "primary",
            FileManagerBusId = _busIds.TryGetValue(AudioModuleKind.FileManager, out var f) ? f : "primary",
            AirBusId = _airBusId ?? string.Empty
        };
        ModuleRoutingStateStore.Save(state);
    }
}

/// -------------
/// Persistencia del module-routing.json.
/// -------------
public static class ModuleRoutingStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigPath { get; } = BuildPath();

    public static ModuleRoutingState? Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ModuleRoutingState>(json, Options);
        }
        catch { return null; }
    }

    public static void Save(ModuleRoutingState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(state, Options));
        }
        catch { }
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "module-routing.json");
    }
}