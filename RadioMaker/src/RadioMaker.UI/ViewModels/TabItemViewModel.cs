using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// Tipo de pestaña. Determina que contenido tiene y como se comporta.
/// </summary>
public enum TabKind
{
    /// <summary>Pestaña de una lista de Programados (.rms). Cerrable y renombrable.</summary>
    Scheduled,

    /// <summary>Pestaña de una Tanda (.tnd).</summary>
    Tanda,

    /// <summary>Pestaña de una Playlist (.rmf).</summary>
    Playlist,

    /// <summary>Pestaña de otro tipo (para futuros modulos).</summary>
    Custom,

    /// <summary>
    /// Pestaña fija de Eventos. No cerrable, no renombrable, no abrir/guardar.
    /// Solo informativa por ahora.
    /// </summary>
    Events,

    /// <summary>
    /// Pestaña de editor de notas (texto plano .txt). Cerrable, con
    /// autosave a draft en %APPDATA%.
    /// </summary>
    Notes,

    /// <summary>
    /// Pestaña de una botonera Cart extra. Cerrable. Solo se ofrece en
    /// la zona central. La botonera principal es la fija 0 del centro.
    /// </summary>
    Cart,

    /// <summary>
    /// Pestaña de una carpeta favorita. Cerrable. Solo se ofrece en la
    /// zona derecha, desde el submenu de favoritos.
    /// </summary>
    FavoriteFolder,
    Encadenadores
}

/// <summary>
/// ViewModel de una pestaña individual.
/// Contiene su titulo editable, un flag de cierre, y el ViewModel del
/// contenido que se muestra cuando esta activa.
/// </summary>
public sealed partial class TabItemViewModel : ViewModelBase
{
    private readonly Action<TabItemViewModel>? _onCloseRequested;

    /// <summary>Identificador unico e inmutable.</summary>
    public string Id { get; }

    /// <summary>Tipo de pestaña.</summary>
    public TabKind Kind { get; }

    /// <summary>
    /// Indica si el usuario puede cerrar esta pestaña.
    /// La pestaña de Programados (Scheduled) siempre tiene IsClosable = false.
    /// </summary>
    public bool IsClosable { get; }

    /// <summary>ViewModel del contenido especifico de esta pestaña.</summary>
    public object? Content { get; set; }

    /// <summary>Titulo visible de la pestaña.</summary>
    [ObservableProperty] private string _title;

    /// <summary>Indica si el usuario esta editando el titulo en este momento.</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>Texto temporal mientras se edita (antes de confirmar).</summary>
    [ObservableProperty] private string _editingTitle = string.Empty;

    /// <summary>
    /// Icono a mostrar a la izquierda del titulo de la pestaña. Se calcula
    /// a partir de <see cref="Kind"/> y no cambia durante la vida del tab.
    /// </summary>
    public MaterialIconKind IconKind => Kind switch
    {
        TabKind.Events => MaterialIconKind.Bell,
        TabKind.Scheduled => MaterialIconKind.CalendarClock,
        TabKind.Tanda => MaterialIconKind.FormatListBulleted,
        TabKind.Playlist => MaterialIconKind.PlaylistMusic,
        TabKind.Cart => MaterialIconKind.Grid,
        TabKind.Notes => MaterialIconKind.NoteText,
        TabKind.FavoriteFolder => MaterialIconKind.FolderStar,
        TabKind.Encadenadores => MaterialIconKind.LinkVariant,
        _ => MaterialIconKind.File,
    };

    /// <summary>Indica si hay cambios sin guardar (muestra un asterisco).</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>Indica si esta pestaña es la activa.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Estado de "al aire" de esta pestaña (para el indicador visual).
    /// </summary>
    [ObservableProperty] private TabLiveState _liveState = TabLiveState.None;

    /// <summary>True si la pestaña esta al aire (reproduciendo).</summary>
    public bool IsLive => LiveState == TabLiveState.Live;

    /// <summary>True si la pestaña esta pausada por el scheduler.</summary>
    public bool IsPausedByScheduler => LiveState == TabLiveState.PausedByScheduler;

    /// <summary>True si hay que mostrar algun indicador (rojo o amarillo).</summary>
    public bool HasLiveIndicator => LiveState != TabLiveState.None;

    /// <summary>Color del indicador segun estado (clave de recurso o hex).</summary>
    public string LiveIndicatorColor => LiveState switch
    {
        TabLiveState.Live => "#EF4444",               // rojo
        TabLiveState.PausedByScheduler => "#F59E0B",  // amarillo
        _ => "Transparent"
    };

    partial void OnLiveStateChanged(TabLiveState value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsPausedByScheduler));
        OnPropertyChanged(nameof(HasLiveIndicator));
        OnPropertyChanged(nameof(LiveIndicatorColor));
    }

    /// <summary>
    /// Texto que muestra la pestaña. NO le agrega un "*" — el "*" ya viene
    /// incorporado en <see cref="Title"/> por los VMs de contenido
    /// (TitleWithDirty). Si agregaramos otro "*" aca, apareceria duplicado
    /// o "fantasma" cuando el VM ya lo limpio.
    /// </summary>
    public string DisplayTitle => Title;

    public TabItemViewModel(
        string title,
        TabKind kind,
        bool isClosable,
        object? content = null,
        Action<TabItemViewModel>? onCloseRequested = null)
    {
        Id = Guid.NewGuid().ToString("N");
        Kind = kind;
        IsClosable = isClosable;
        Content = content;
        _title = title;
        _editingTitle = title;
        _onCloseRequested = onCloseRequested;
    }

    /// <summary>Solicita cerrar esta pestaña. El TabHost decide si procede.</summary>
    [RelayCommand]
    private void RequestClose()
    {
        if (!IsClosable) return;
        _onCloseRequested?.Invoke(this);
    }

    /// <summary>
    /// Se dispara cuando el usuario confirma un nuevo titulo para la
    /// pestaña (despues de CommitEdit). El TabHost lo usa para sincronizar
    /// el titulo con el contenido: por ejemplo, actualizar el BoardName
    /// de un CartBoardViewModel, el PlaylistName de un PlaylistEditorViewModel,
    /// el TandaName de un TandaEditorViewModel, el ListName de un
    /// ScheduledListViewModel.
    /// </summary>
    public event EventHandler<string>? TitleCommitted;

    /// <summary>Inicia la edicion inline del titulo.</summary>
    public void BeginEdit()
    {
        if (!IsClosable) return;
        EditingTitle = Title;
        IsEditing = true;
    }

    /// <summary>Confirma la edicion y aplica el nuevo titulo.</summary>
    public void CommitEdit()
    {
        if (!IsEditing) return;

        var newTitle = EditingTitle?.Trim();
        if (string.IsNullOrWhiteSpace(newTitle))
            newTitle = "Sin nombre";

        Title = newTitle;
        IsEditing = false;

        // NO tocamos IsDirty aca: el renombre se propaga al VM de contenido
        // via TitleCommitted, y ese VM marca su propio IsDirty. Si ademas
        // marcaramos el TabItemViewModel.IsDirty, quedaria pegado y mostraria
        // un "*" que nunca se limpia al guardar.
        try { TitleCommitted?.Invoke(this, newTitle); } catch { }
    }

    /// <summary>Cancela la edicion sin aplicar cambios.</summary>
    public void CancelEdit()
    {
        EditingTitle = Title;
        IsEditing = false;
    }

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
}
