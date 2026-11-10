using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del editor de un boton de la botonera.
///
/// Trabaja sobre un SNAPSHOT de los valores del boton original. Los cambios
/// se aplican solo cuando se llama a ApplyToTarget (boton Guardar). Si el
/// usuario cancela, no se toca nada.
/// </summary>
public sealed partial class CartButtonEditorViewModel : ViewModelBase
{
    private readonly CartButtonViewModel _target;

    // ───── Propiedades editables ─────────────────────────────────────────

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _color;
    [ObservableProperty] private string _hotkey;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _volumeDb;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private string _notes;

    // ───── Propiedades de presentacion ───────────────────────────────────

    public string Header => $"Editar boton: {_target.DisplayLabel}";

    public string DurationText => _target.DurationText;
    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    public string VolumeText => VolumeDb == 0.0
        ? "0.0 dB"
        : VolumeDb > 0
            ? $"+{VolumeDb:F1} dB"
            : $"{VolumeDb:F1} dB";

    /// <summary>Colores rapidos predefinidos para el picker.</summary>
    public string[] QuickColors { get; } = new[]
    {
        "#2A3050",  // azul oscuro (default)
        "#7C3F58",  // vino
        "#3F5C3A",  // verde oscuro
        "#5C4A2A",  // marron
        "#3A2E5A",  // violeta
        "#1F3A52",  // azul petroleo
        "#4D3B1F",  // ambar oscuro
        "#5A1F1F",  // rojo oscuro
    };

    // ───── Constructor ───────────────────────────────────────────────────

    public CartButtonEditorViewModel(CartButtonViewModel target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));

        // Snapshot inicial.
        _filePath = target.FilePath;
        _label = target.Label;
        _color = target.Color;
        _hotkey = target.Hotkey;
        _isEnabled = target.IsEnabled;
        _volumeDb = target.VolumeDb;
        _loop = target.Loop;
        _notes = target.Notes;
    }

    // ───── Aplicacion al boton original ──────────────────────────────────

    /// <summary>
    /// Copia los valores del editor al boton original.
    /// Se llama cuando el usuario confirma.
    /// </summary>
    public void ApplyToTarget()
    {
        _target.FilePath = FilePath ?? string.Empty;
        _target.Label = Label ?? string.Empty;
        _target.Color = string.IsNullOrWhiteSpace(Color) ? "#2A3050" : Color;
        _target.Hotkey = Hotkey ?? string.Empty;
        _target.IsEnabled = IsEnabled;
        _target.VolumeDb = VolumeDb;
        _target.Loop = Loop;
        _target.Notes = Notes ?? string.Empty;
    }

    // ───── Comandos auxiliares ───────────────────────────────────────────

    /// <summary>Aplica un color rapido al editor (no al target todavia).</summary>
    public void SetQuickColor(string color)
    {
        if (!string.IsNullOrWhiteSpace(color))
            Color = color;
    }

    /// <summary>Limpia el archivo y el label.</summary>
    public void ClearFile()
    {
        FilePath = string.Empty;
        Label = string.Empty;
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(DurationText));
    }

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(DurationText));
    }

    partial void OnVolumeDbChanged(double value) => OnPropertyChanged(nameof(VolumeText));
}