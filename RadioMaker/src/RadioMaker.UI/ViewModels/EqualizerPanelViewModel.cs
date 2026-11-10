using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;
using RadioMaker.UI.Configuration;

namespace RadioMaker.UI.ViewModels;

public sealed partial class EqualizerPanelViewModel : ViewModelBase
{
    private readonly EqualizerSettings _settings;
    private IAudioChannel? _channel;
    private bool _suppressApply;
    /// <summary>
    /// Se dispara cuando el usuario cambia alguna banda, activa/desactiva el EQ,
    /// cambia de preset o resetea. Se usa para propagar los cambios a TODOS
    /// los canales activos de la app (manual, scheduler, playlist, cart).
    /// </summary>
    public event EventHandler? SettingsChanged;

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();
    public ObservableCollection<string> AvailablePresets { get; } = new();

    [ObservableProperty] private string? _selectedPreset;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _newPresetName = string.Empty;

    public bool CanDeletePreset =>
        SelectedPreset is not null &&
        _settings.IsCustomPreset(SelectedPreset);

    public bool CanSavePreset => !string.IsNullOrWhiteSpace(NewPresetName);

    public EqualizerPanelViewModel(EqualizerSettings settings)
    {
        _settings = settings;
        _isEnabled = settings.IsEnabled;
        _selectedPreset = settings.PresetName;

        RebuildPresets();

        for (int i = 0; i < EqualizerSettings.BandCount; i++)
        {
            var vm = new EqBandViewModel(
                index: i,
                label: EqualizerSettings.FrequencyLabels[i],
                initialGain: settings.Gains[i],
                minGain: settings.MinGain,
                maxGain: settings.MaxGain);

            vm.GainChanged += OnBandGainChanged;
            Bands.Add(vm);
        }

        _settings.AllBandsChanged += OnSettingsAllBandsChanged;
        _settings.CustomPresetsChanged += (_, _) => RebuildPresets();
    }

    private void RebuildPresets()
    {
        _suppressApply = true;
        try
        {
            var previous = SelectedPreset;
            AvailablePresets.Clear();

            foreach (var p in EqualizerSettings.Presets.Keys)
                AvailablePresets.Add(p);

            foreach (var p in _settings.CustomPresets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AvailablePresets.Add(p);

            if (previous is not null && AvailablePresets.Contains(previous))
            {
                SelectedPreset = previous;
            }
            else if (!string.IsNullOrEmpty(_settings.PresetName) &&
                     AvailablePresets.Contains(_settings.PresetName))
            {
                SelectedPreset = _settings.PresetName;
            }
        }
        finally
        {
            _suppressApply = false;
        }

        OnPropertyChanged(nameof(CanDeletePreset));
    }

    public void AttachToChannel(IAudioChannel? channel)
    {
        _channel = channel;

        if (channel is null)
        {
            StatusMessage = "Sin canal cargado. Cargá un archivo para escuchar el EQ.";
            return;
        }

        try
        {
            // Aplicar primero las ganancias, luego habilitar/deshabilitar.
            // De esta forma, si el EQ arranca enabled, el FX se crea con los
            // valores correctos desde el primer instante.
            channel.Equalizer.ApplyGains(_settings.Gains);
            channel.Equalizer.SetEnabled(IsEnabled);
            StatusMessage = IsEnabled
                ? "EQ activo sobre el canal actual."
                : "EQ deshabilitado (activá para escuchar cambios).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al aplicar EQ: {ex.Message}";
        }
    }

    private void OnBandGainChanged(object? sender, int bandIndex)
    {
        if (_suppressApply) return;
        if (sender is not EqBandViewModel vm) return;

        _settings.SetBandGain(bandIndex, vm.Gain);

        try { _channel?.Equalizer.SetBandGain(bandIndex, vm.Gain); }
        catch { }

        // Comparar contra la propiedad generada, no contra el backing field.
        if (SelectedPreset != _settings.PresetName)
        {
            _suppressApply = true;
            SelectedPreset = _settings.PresetName;
            _suppressApply = false;
        }

        // Notificar al exterior que hubo cambios.
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsAllBandsChanged(object? sender, EventArgs e)
    {
        _suppressApply = true;
        try
        {
            for (int i = 0; i < Bands.Count; i++)
                Bands[i].Gain = _settings.Gains[i];

            // Asignar a la propiedad generada (no al backing field).
            // Como _suppressApply es true, el setter de la propiedad no
            // dispara OnSelectedPresetChanged -> no hay recursion.
            // El setter ya se encarga de notificar el cambio (no hace falta
            // el OnPropertyChanged manual que estaba antes).
            SelectedPreset = _settings.PresetName;
        }
        finally { _suppressApply = false; }

        try { _channel?.Equalizer.ApplyGains(_settings.Gains); }
        catch { }

        StatusMessage = $"Preset aplicado: {_settings.PresetName}";
        OnPropertyChanged(nameof(CanDeletePreset));

        // Notificar al exterior.
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _settings.IsEnabled = value;

        // Persistir el estado enabled en disco para el proximo arranque.
        try { EqualizerSettingsStore.Save(_settings); } catch { }

        try
        {
            if (_channel is not null)
            {
                _channel.Equalizer.SetEnabled(value);
                StatusMessage = value ? "EQ activo." : "EQ deshabilitado.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }

        // Notificar al exterior.
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedPresetChanged(string? value)
    {
        OnPropertyChanged(nameof(CanDeletePreset));

        if (_suppressApply || value is null) return;
        if (value == _settings.PresetName) return;

        _settings.ApplyPreset(value);

        // Persistir el cambio en disco.
        try { EqualizerSettingsStore.Save(_settings); } catch { }

        StatusMessage = $"Preset aplicado: {value}";
    }

    partial void OnNewPresetNameChanged(string value) =>
        OnPropertyChanged(nameof(CanSavePreset));

    [RelayCommand]
    private void Reset()
    {
        _settings.ResetToFlat();
        StatusMessage = "Todas las bandas reseteadas a 0 dB.";

        // Notificar al exterior.
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = NewPresetName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Ingresá un nombre para el preset.";
            return;
        }

        if (EqualizerSettings.Presets.ContainsKey(name))
        {
            StatusMessage = $"'{name}' es un preset de fabrica. Elegí otro nombre.";
            return;
        }

        _settings.SaveCustomPreset(name);
        EqualizerSettingsStore.Save(_settings);

        NewPresetName = string.Empty;
        StatusMessage = $"Preset '{name}' guardado.";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is null) return;
        if (!_settings.IsCustomPreset(SelectedPreset))
        {
            StatusMessage = "Solo se pueden eliminar presets personalizados.";
            return;
        }

        var name = SelectedPreset;
        if (_settings.DeleteCustomPreset(name))
        {
            EqualizerSettingsStore.Save(_settings);
            StatusMessage = $"Preset '{name}' eliminado.";
        }
    }
}

public sealed partial class EqBandViewModel : ViewModelBase
{
    private readonly double _min;
    private readonly double _max;

    public int Index { get; }
    public string Label { get; }

    [ObservableProperty] private double _gain;

    public string GainText => Gain >= 0 ? $"+{Gain:F1}" : $"{Gain:F1}";

    public event EventHandler<int>? GainChanged;

    public EqBandViewModel(int index, string label, double initialGain, double minGain, double maxGain)
    {
        Index = index;
        Label = label;
        _min = minGain;
        _max = maxGain;
        _gain = Math.Clamp(initialGain, _min, _max);
    }

    partial void OnGainChanged(double value)
    {
        OnPropertyChanged(nameof(GainText));
        GainChanged?.Invoke(this, Index);
    }
}
