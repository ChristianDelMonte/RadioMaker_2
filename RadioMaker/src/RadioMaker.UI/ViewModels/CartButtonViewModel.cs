using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Wrapper de UI sobre <see cref="CartButton"/>.
///
/// Expone las propiedades visuales del boton (label, color, estado) y
/// se encarga de leer la duracion del archivo cuando se asigna.
/// -------------
public sealed partial class CartButtonViewModel : ViewModelBase, IDisposable
{
    private readonly CartButton _button;

    /// -------------Handler cacheado para poder desuscribirse en Dispose.</summary>
    private readonly EventHandler _themeChangedHandler;

    private bool _disposed;

    /// -------------
    /// Cyan brillante del display LED. Se usa como acento para el highlight
    /// de drag &amp; drop, para unificar con el color del VU/LEDs.
    /// -------------
    private const string DragOverBorderHex = "#00E5FF";

    /// -------------
    /// Mismo cyan con ~20% de opacidad. Se usa como fondo del highlight
    /// (formato ARGB).
    /// -------------
    private const string DragOverBackgroundHex = "#3300E5FF";

    public CartButton Source => _button;

    public event EventHandler? Changed;

    // ───── Propiedades observables ───────────────────────────────────────

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _color;
    [ObservableProperty] private string _hotkey;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _volumeDb;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private string _notes;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isSelected;

    /// -------------
    /// True mientras el usuario esta arrastrando un archivo valido SOBRE
    /// este boton. Se usa para el highlight visual de drag &amp; drop.
    /// -------------
    [ObservableProperty] private bool _isDragOver;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public double DurationSeconds => _button.DurationSeconds;
    public string DurationText => AudioFileInfoReader.FormatDuration(DurationSeconds);

    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label)
            ? Label
            : (!string.IsNullOrWhiteSpace(FilePath)
                ? Path.GetFileNameWithoutExtension(FilePath)
                : string.Empty);

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);
    public bool HasNoFile => !HasFile;
    public bool HasHotkey => !string.IsNullOrWhiteSpace(Hotkey);

    public string VolumeText => VolumeDb == 0.0
        ? "0.0 dB"
        : VolumeDb > 0
            ? $"+{VolumeDb:F1} dB"
            : $"{VolumeDb:F1} dB";

    /// -------------
    /// Color de fondo efectivo.
    ///
    /// Prioridad:
    ///   1. IsDragOver  -> cyan translucido (highlight).
    ///   2. IsPlaying   -> rojo oscuro (resource CartPlayingBg).
    ///   3. IsSelected  -> color del boton aclarado.
    ///   4. !IsEnabled  -> gris.
    ///   5. Caso normal -> color elegido por el usuario.
    /// -------------
    public string EffectiveBackground
    {
        get
        {
            if (IsDragOver) return DragOverBackgroundHex;

            // Rojo FIJO, independiente del tema. Antes usabamos la clave
            // "CartPlayingBg" que en tema claro se resolvia a un rosa claro.
            // Con este rojo oscuro el texto blanco se lee bien en ambos temas.
            if (IsPlaying) return "#991B1B";

            if (IsSelected) return Lighten(Color, 0.25);
            if (!IsEnabled) return "BgPrimary";
            return Color;
        }
    }

    /// -------------
    /// Color del borde efectivo.
    ///
    /// Prioridad:
    ///   1. IsDragOver -> cyan brillante (LED color).
    ///   2. IsPlaying  -> rojo brillante.
    ///   3. IsSelected -> acento.
    ///   4. Caso normal -> borde sutil.
    /// -------------
    public string EffectiveBorderColor
    {
        get
        {
            if (IsDragOver) return DragOverBorderHex;

            // Rojo brillante fijo (mismo en ambos temas).
            if (IsPlaying) return "#EF4444";

            if (IsSelected) return "Accent";
            return "BorderStrong";
        }
    }

    /// -------------
    /// Fondo del badge "REPRODUCIENDO" que se muestra cuando el cart esta
    /// sonando. Rojo brillante fijo, mismo en ambos temas.
    /// -------------
    public string PlayingBadgeBackground => "#EF4444";

    /// -------------
    /// Grosor del borde efectivo.
    ///
    /// Prioridad:
    ///   1. IsDragOver -> 3.0 (mas grueso que playing/selected).
    ///   2. IsPlaying o IsSelected -> 2.5.
    ///   3. Caso normal -> 1.0.
    /// -------------
    public double EffectiveBorderThickness
    {
        get
        {
            if (IsDragOver) return 3.0;
            if (IsPlaying) return 2.5;
            if (IsSelected) return 2.5;
            return 1.0;
        }
    }

    private static string Lighten(string hex, double amount)
    {
        try
        {
            var c = Avalonia.Media.Color.Parse(hex);
            byte r = (byte)Math.Min(255, c.R + (255 - c.R) * amount);
            byte g = (byte)Math.Min(255, c.G + (255 - c.G) * amount);
            byte b = (byte)Math.Min(255, c.B + (255 - c.B) * amount);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return hex; }
    }

    /// -------------Opacidad del texto: 0.4 si esta deshabilitado, 1.0 si no.</summary>
    public double EffectiveOpacity => IsEnabled ? 1.0 : 0.4;

    /// -------------Muestra la etiqueta o un placeholder si no hay nada.</summary>
    public string ButtonText => HasFile ? DisplayLabel : "(vacio)";

    // ───── Constructor ───────────────────────────────────────────────────

    public CartButtonViewModel(CartButton button)
    {
        _button = button;

        _filePath = button.FilePath;
        _label = button.Label;
        _color = button.Color;
        _hotkey = button.Hotkey;
        _isEnabled = button.IsEnabled;
        _volumeDb = button.VolumeDb;
        _loop = button.Loop;
        _notes = button.Notes;

        if (button.DurationSeconds <= 0 && !string.IsNullOrWhiteSpace(button.FilePath))
            _ = RefreshDurationAsync(button.FilePath);

        _themeChangedHandler = OnThemeChanged;
        RadioMaker.UI.Services.ThemeService.ThemeChanged += _themeChangedHandler;
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(EffectiveBackground));
            OnPropertyChanged(nameof(EffectiveBorderColor));
        });
    }

    // ───── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { RadioMaker.UI.Services.ThemeService.ThemeChanged -= _themeChangedHandler; }
        catch { }
    }

    public void ApplyToModel()
    {
        _button.FilePath = FilePath ?? string.Empty;
        _button.Label = Label ?? string.Empty;
        _button.Color = Color ?? "#2A3050";
        _button.Hotkey = Hotkey ?? string.Empty;
        _button.IsEnabled = IsEnabled;
        _button.VolumeDb = VolumeDb;
        _button.Loop = Loop;
        _button.Notes = Notes ?? string.Empty;
    }

    /// -------------Limpia el boton (borra archivo y label).</summary>
    public void Clear()
    {
        FilePath = string.Empty;
        Label = string.Empty;
        _button.DurationSeconds = 0;

        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        NotifyChanged();
    }

    /// -------------
    /// True si el Label actual parece ser "auto-generado" a partir del
    /// FilePath (o sea, el usuario no lo personalizo). Se usa para decidir
    /// si conviene pisarlo al reemplazar el audio.
    ///
    /// Criterio:
    ///   - Label vacio -> auto (nada que respetar).
    ///   - Label == nombre del FilePath sin extension -> auto.
    ///   - Cualquier otra cosa -> custom (no se pisa).
    /// -------------
    public bool IsLabelAutoGenerated()
    {
        if (string.IsNullOrWhiteSpace(Label))
            return true;

        if (string.IsNullOrWhiteSpace(FilePath))
            return false;

        var autoName = Path.GetFileNameWithoutExtension(FilePath);
        return string.Equals(Label, autoName, StringComparison.Ordinal);
    }

    // ───── Lectura de duracion ───────────────────────────────────────────

    private async Task RefreshDurationAsync(string path)
    {
        double seconds = 0;
        try
        {
            seconds = await Task.Run(() => AudioFileInfoReader.GetDurationSeconds(path));
        }
        catch { }

        if (!string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase)) return;

        _button.DurationSeconds = seconds;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        NotifyChanged();
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveBorderColor));
        OnPropertyChanged(nameof(EffectiveBorderThickness));
        OnPropertyChanged(nameof(EffectiveBackground));
        OnPropertyChanged(nameof(PlayingBadgeBackground));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveBorderColor));
        OnPropertyChanged(nameof(EffectiveBorderThickness));
        OnPropertyChanged(nameof(EffectiveBackground));
    }

    partial void OnIsDragOverChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveBorderColor));
        OnPropertyChanged(nameof(EffectiveBorderThickness));
        OnPropertyChanged(nameof(EffectiveBackground));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(EffectiveBackground));
        OnPropertyChanged(nameof(EffectiveOpacity));
        NotifyChanged();
    }

    partial void OnFilePathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(HasNoFile));
        OnPropertyChanged(nameof(ButtonText));

        _button.DurationSeconds = 0;
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));

        if (!string.IsNullOrWhiteSpace(value))
            _ = RefreshDurationAsync(value);

        NotifyChanged();
    }

    partial void OnLabelChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(ButtonText));
        NotifyChanged();
    }

    partial void OnColorChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(EffectiveBackground));
        NotifyChanged();
    }

    partial void OnHotkeyChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(HasHotkey));
        NotifyChanged();
    }

    partial void OnVolumeDbChanged(double value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(VolumeText));
        NotifyChanged();
    }

    partial void OnLoopChanged(bool value) { ApplyToModel(); NotifyChanged(); }
    partial void OnNotesChanged(string value) { ApplyToModel(); NotifyChanged(); }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}