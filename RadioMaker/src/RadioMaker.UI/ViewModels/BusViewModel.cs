using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioMaker.Core.Abstractions;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel de un Bus individual, usado dentro de la ventana de configuración.
/// -------------
public sealed partial class BusViewModel : ViewModelBase
{
    private readonly IAudioBus _bus;
    private readonly Func<IReadOnlyList<AudioDeviceInfo>> _deviceEnumerator;
    private bool _suppressApply;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private double _masterVolume;
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isPrimary;

    public ObservableCollection<AudioDeviceInfo> AvailableDevices { get; } = new();

    public string Id => _bus.Id;
    public bool CanRemove => !IsPrimary;
    public bool CanChangeDevice => !IsPrimary; // el bus primario no cambia de device desde aquí

    public event EventHandler? RemoveRequested;

    public BusViewModel(
        IAudioBus bus,
        bool isPrimary,
        Func<IReadOnlyList<AudioDeviceInfo>> deviceEnumerator)
    {
        _bus = bus;
        _deviceEnumerator = deviceEnumerator;
        _displayName = bus.DisplayName;
        _masterVolume = bus.MasterVolume;
        _isPrimary = isPrimary;

        RefreshDevices();
    }

    public void RefreshDevices()
    {
        var wasSuppressed = _suppressApply;
        _suppressApply = true;
        try
        {
            AvailableDevices.Clear();
            foreach (var d in _deviceEnumerator())
                AvailableDevices.Add(d);

            var target = AvailableDevices.FirstOrDefault(d => d.Index == _bus.DeviceIndex)
                         ?? AvailableDevices.FirstOrDefault();

            if (!ReferenceEquals(SelectedDevice, target))
                SelectedDevice = target;
        }
        finally
        {
            _suppressApply = wasSuppressed;
        }
    }

    [RelayCommand]
    private void TestTone()
    {
        try
        {
            _bus.PlayTestTone(440.0, 1000);
            StatusMessage = $"Tono de prueba enviado (440 Hz, 1 s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Remove()
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- Sincronización con el bus ---------------------------------------

    partial void OnDisplayNameChanged(string value)
    {
        if (_suppressApply) return;
        _bus.DisplayName = value;
    }

    partial void OnMasterVolumeChanged(double value)
    {
        if (_suppressApply) return;
        _bus.MasterVolume = value;
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_suppressApply || value is null) return;

        // El bus primario no puede cambiar dispositivo desde esta ventana.
        // Ignorar para no romper la reproduccion en curso.
        if (IsPrimary) return;

        if (value.Index == _bus.DeviceIndex) return;

        try
        {
            _bus.ChangeDevice(value.Index);
            StatusMessage = $"Dispositivo cambiado a: {value.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cambiar dispositivo: {ex.Message}";
        }
    }
}
