using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel de un item dentro de un Bloque de Programacion.
///
/// Es mucho mas simple que un ScheduledItemViewModel: los items dentro de
/// un bloque NO tienen horarios, prioridad ni activo/desactivo. Solo tienen
/// tipo, archivo y titulo.
///
/// Ademas calcula automaticamente la duracion del contenido:
///   - SingleFile: lee el header del archivo de audio.
///   - Playlist:   carga el .rmf y suma las duraciones de sus pistas.
///   - Tanda:      carga el .tnd y suma las duraciones de sus pistas.
///
/// El resultado se cachea en el modelo (DurationSeconds) para no recalcular
/// cuando se reabre el editor.
/// </summary>
public sealed partial class ScheduledBlockItemViewModel : ViewModelBase
{
    private readonly ScheduledBlockItem _item;

    public ScheduledBlockItem Source => _item;

    /// <summary>Se dispara cuando cambia cualquier propiedad relevante.</summary>
    public event EventHandler? Changed;

    // ───── Propiedades observables ───────────────────────────────────────

    [ObservableProperty] private ScheduledItemKind _kind;
    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _title;

    /// <summary>
    /// Duracion total del contenido en segundos. Se recalcula cada vez que
    /// cambia el Kind o el FilePath. Se inicializa con el valor cacheado en
    /// el modelo (si lo hay).
    /// </summary>
    [ObservableProperty] private double _durationSeconds;

    // ───── Propiedades derivadas ─────────────────────────────────────────

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : (!string.IsNullOrWhiteSpace(FilePath)
                ? (Kind == ScheduledItemKind.SingleFile
                    ? Path.GetFileName(FilePath)
                    : Path.GetFileNameWithoutExtension(FilePath))
                : "(sin contenido)");

    public string KindText => Kind switch
    {
        ScheduledItemKind.Playlist => "PLAYLIST",
        ScheduledItemKind.Tanda => "TANDA",
        _ => "ARCHIVO"
    };

    public string KindColor => Kind switch
    {
        ScheduledItemKind.Playlist => "Success",
        ScheduledItemKind.Tanda => "Accent",
        _ => "FgMuted"
    };

    public bool HasContent => !string.IsNullOrWhiteSpace(FilePath);

    /// <summary>Duracion formateada como HH:MM:SS.</summary>
    public string DurationText =>
        DurationSeconds > 0.5
            ? AudioFileInfoReader.FormatDuration(DurationSeconds)
            : (HasContent ? "—" : string.Empty);

    // ───── Constructor ───────────────────────────────────────────────────

    public ScheduledBlockItemViewModel(ScheduledBlockItem item)
    {
        _item = item;

        _kind = item.Kind;
        _filePath = item.FilePath;
        _title = item.Title;
        _durationSeconds = item.DurationSeconds;

        // Si no tenemos duracion cacheada pero hay contenido, calcularla.
        if (_durationSeconds <= 0.5 && !string.IsNullOrWhiteSpace(item.FilePath))
        {
            _ = RefreshDurationAsync();
        }
    }

    // ───── Calculo de duracion ───────────────────────────────────────────

    /// <summary>
    /// Recalcula la duracion del item en background. Depende de:
    ///   - SingleFile: lee el header del audio.
    ///   - Playlist:   carga el .rmf y suma duraciones de pistas habilitadas.
    ///   - Tanda:      carga el .tnd y suma duraciones de pistas habilitadas.
    ///
    /// Si el archivo es inaccesible o esta vacio, deja 0.
    /// </summary>
    private async Task RefreshDurationAsync()
    {
        // Snapshot de los valores con los que arranca el calculo.
        string path = FilePath ?? string.Empty;
        ScheduledItemKind kind = Kind;

        if (string.IsNullOrWhiteSpace(path))
        {
            _item.DurationSeconds = 0;
            DurationSeconds = 0;
            return;
        }

        double seconds = 0;
        try
        {
            seconds = await Task.Run(() =>
            {
                try
                {
                    return kind switch
                    {
                        ScheduledItemKind.SingleFile => AudioFileInfoReader.GetDurationSeconds(path),
                        ScheduledItemKind.Playlist => GetPlaylistDuration(path),
                        ScheduledItemKind.Tanda => GetTandaDuration(path),
                        _ => 0
                    };
                }
                catch
                {
                    return 0;
                }
            });
        }
        catch { }

        // Si mientras calculabamos cambio el Kind o el FilePath, descartar
        // el resultado (ya se disparo otro calculo).
        if (_item.Kind != kind) return;
        if (!string.Equals(_item.FilePath, path, StringComparison.OrdinalIgnoreCase)) return;

        _item.DurationSeconds = seconds;
        DurationSeconds = seconds;
    }

    /// <summary>
    /// Suma las duraciones de las pistas habilitadas de un .rmf.
    /// Usa el DurationSeconds cacheado en cada pista si esta disponible;
    /// si no, lo lee del archivo.
    /// </summary>
    private static double GetPlaylistDuration(string rmfPath)
    {
        if (!File.Exists(rmfPath)) return 0;

        Playlist? playlist = null;
        try { playlist = PlaylistStore.Load(rmfPath); } catch { }
        if (playlist is null) return 0;

        double total = 0;
        foreach (var pt in playlist.Tracks)
        {
            if (!pt.IsEnabled) continue;
            if (string.IsNullOrWhiteSpace(pt.FilePath)) continue;

            double d = pt.DurationSeconds;
            if (d <= 0.5)
            {
                try { d = AudioFileInfoReader.GetDurationSeconds(pt.FilePath); } catch { d = 0; }
            }
            total += d;
        }
        return total;
    }

    /// <summary>
    /// Suma las duraciones de las pistas habilitadas de un .tnd.
    /// Usa el DurationSeconds cacheado en cada pista si esta disponible;
    /// si no, lo lee del archivo.
    /// </summary>
    private static double GetTandaDuration(string tndPath)
    {
        if (!File.Exists(tndPath)) return 0;

        Tanda? tanda = null;
        try { tanda = TandaStore.Load(tndPath); } catch { }
        if (tanda is null) return 0;

        double total = 0;
        foreach (var t in tanda.Tracks)
        {
            if (!t.IsEnabled) continue;
            if (string.IsNullOrWhiteSpace(t.FilePath)) continue;

            double d = t.DurationSeconds;
            if (d <= 0.5)
            {
                try { d = AudioFileInfoReader.GetDurationSeconds(t.FilePath); } catch { d = 0; }
            }
            total += d;
        }
        return total;
    }

    // ───── Sincronizacion ────────────────────────────────────────────────

    public void ApplyToModel()
    {
        _item.Kind = Kind;
        _item.FilePath = FilePath ?? string.Empty;
        _item.Title = Title ?? string.Empty;
        _item.DurationSeconds = DurationSeconds;
    }

    public void CopyFrom(ScheduledBlockItemViewModel other)
    {
        if (other is null) return;

        Kind = other.Kind;
        FilePath = other.FilePath;
        Title = other.Title;

        ApplyToModel();
        NotifyChanged();
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnKindChanged(ScheduledItemKind value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(KindColor));

        // Al cambiar el tipo, la duracion ya no es valida: reset + recalc.
        _item.DurationSeconds = 0;
        DurationSeconds = 0;

        if (!string.IsNullOrWhiteSpace(FilePath))
            _ = RefreshDurationAsync();

        NotifyChanged();
    }

    partial void OnFilePathChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(HasContent));

        // Al cambiar el archivo, recalcular duracion.
        _item.DurationSeconds = 0;
        DurationSeconds = 0;

        if (!string.IsNullOrWhiteSpace(value))
            _ = RefreshDurationAsync();

        NotifyChanged();
    }

    partial void OnTitleChanged(string value)
    {
        ApplyToModel();
        OnPropertyChanged(nameof(DisplayTitle));
        NotifyChanged();
    }

    partial void OnDurationSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(DurationText));
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}