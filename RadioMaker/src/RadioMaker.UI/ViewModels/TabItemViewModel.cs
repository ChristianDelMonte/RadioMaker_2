using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Tipo de pestaña. Determina que contenido tiene y como se comporta.
/// -------------
public enum TabKind
{
    /// -------------Pestaña de una lista de Programados (.rms). Cerrable y renombrable.</summary>
    Scheduled,

    /// -------------Pestaña de una Tanda (.tnd).</summary>
    Tanda,

    /// -------------Pestaña de una Playlist (.rmf).</summary>
    Playlist,

    /// -------------Pestaña de otro tipo (para futuros modulos).</summary>
    Custom,

    /// -------------
    /// Pestaña fija de Eventos. No cerrable, no renombrable, no abrir/guardar.
    /// Solo informativa por ahora.
    /// -------------
    Events,

    /// -------------
    /// Pestaña de editor de notas (texto plano .txt). Cerrable, con
    /// autosave a draft en %APPDATA%.
    /// -------------
    Notes,

    /// -------------
    /// Pestaña de una botonera Cart extra. Cerrable. Solo se ofrece en
    /// la zona central. La botonera principal es la fija 0 del centro.
    /// -------------
    Cart,

    /// -------------
    /// Pestaña de una carpeta favorita. Cerrable. Solo se ofrece en la
    /// zona derecha, desde el submenu de favoritos.
    /// -------------
    FavoriteFolder,
    Encadenadores
}

/// -------------
/// ViewModel de una pestaña individual.
/// Contiene su titulo editable, un flag de cierre, y el ViewModel del
/// contenido que se muestra cuando esta activa.
/// -------------
public sealed partial class TabItemViewModel : ViewModelBase
{
    private readonly Action<TabItemViewModel>? _onCloseRequested;

    /// -------------Identificador unico e inmutable.</summary>
    public string Id { get; }

    /// -------------Tipo de pestaña.</summary>
    public TabKind Kind { get; }

    /// -------------
    /// Indica si el usuario puede cerrar esta pestaña.
    /// La pestaña de Programados (Scheduled) siempre tiene IsClosable = false.
    /// -------------
    public bool IsClosable { get; }

    /// -------------ViewModel del contenido especifico de esta pestaña.</summary>
    public object? Content { get; set; }

    public string? LocalizedTitleKey { get; }

    /// -------------Titulo visible de la pestaña.</summary>
    [ObservableProperty] private string _title;

    /// -------------Indica si el usuario esta editando el titulo en este momento.</summary>
    [ObservableProperty] private bool _isEditing;

    /// -------------Texto temporal mientras se edita (antes de confirmar).</summary>
    [ObservableProperty] private string _editingTitle = string.Empty;

    /// -------------
    /// Icono a mostrar a la izquierda del titulo de la pestaña. Se calcula
    /// a partir de <see cref="Kind"/> y no cambia durante la vida del tab.
    /// -------------
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

    /// -------------Indica si hay cambios sin guardar (muestra un asterisco).</summary>
    [ObservableProperty] private bool _isDirty;

    /// -------------Indica si esta pestaña es la activa.</summary>
    [ObservableProperty] private bool _isSelected;

    /// -------------
    /// Estado de "al aire" de esta pestaña (para el indicador visual).
    /// -------------
    [ObservableProperty] private TabLiveState _liveState = TabLiveState.None;

    /// -------------True si la pestaña esta al aire (reproduciendo).</summary>
    public bool IsLive => LiveState == TabLiveState.Live;

    /// -------------True si la pestaña esta pausada por el scheduler.</summary>
    public bool IsPausedByScheduler => LiveState == TabLiveState.PausedByScheduler;

    /// -------------True si hay que mostrar algun indicador (rojo o amarillo).</summary>
    public bool HasLiveIndicator => LiveState != TabLiveState.None;

    /// -------------Color del indicador segun estado (clave de recurso o hex).</summary>
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

    /// -------------
    /// Texto que muestra la pestaña. NO le agrega un "*" — el "*" ya viene
    /// incorporado en <see cref="Title"/> por los VMs de contenido
    /// (TitleWithDirty). Si agregaramos otro "*" aca, apareceria duplicado
    /// o "fantasma" cuando el VM ya lo limpio.
    /// -------------
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(LocalizedTitleKey))
                return LocalizationService.GetString(LocalizedTitleKey);

            var keys = Kind switch
            {
                TabKind.Events => new[] { "Loc.Tabs.Events" },
                TabKind.Scheduled => new[] { "Loc.ScheduledList.DefaultName", "Loc.Tabs.Default.Scheduled" },
                TabKind.Tanda => new[] { "Loc.TandaEditor.NewTanda", "Loc.Tabs.Default.Sequence" },
                TabKind.Playlist => new[] { "Loc.PlaylistEditor.NewPlaylist", "Loc.Tabs.Default.Playlist" },
                TabKind.Encadenadores => new[] { "Loc.Chainers.DefaultName" },
                TabKind.Cart => new[] { "Loc.Carts.DefaultBoardName" },
                TabKind.Notes => new[] { "Loc.Tabs.NewNote" },
                _ => Array.Empty<string>()
            };

            var dirtySuffix = Title.EndsWith(" *", StringComparison.Ordinal) ? " *" : string.Empty;
            var plainTitle = dirtySuffix.Length == 0 ? Title : Title[..^dirtySuffix.Length];
            return LocalizationService.LocalizeKnownDefault(plainTitle, keys) + dirtySuffix;
        }
    }

    public TabItemViewModel(
        string title,
        TabKind kind,
        bool isClosable,
        object? content = null,
        Action<TabItemViewModel>? onCloseRequested = null,
        string? localizedTitleKey = null)
    {
        Id = Guid.NewGuid().ToString("N");
        Kind = kind;
        IsClosable = isClosable;
        Content = content;
        LocalizedTitleKey = localizedTitleKey;
        _title = title;
        _editingTitle = title;
        _onCloseRequested = onCloseRequested;
    }

    /// -------------Solicita cerrar esta pestaña. El TabHost decide si procede.</summary>
    [RelayCommand]
    private void RequestClose()
    {
        if (!IsClosable) return;
        _onCloseRequested?.Invoke(this);
    }

    /// -------------
    /// Se dispara cuando el usuario confirma un nuevo titulo para la
    /// pestaña (despues de CommitEdit). El TabHost lo usa para sincronizar
    /// el titulo con el contenido: por ejemplo, actualizar el BoardName
    /// de un CartBoardViewModel, el PlaylistName de un PlaylistEditorViewModel,
    /// el TandaName de un TandaEditorViewModel, el ListName de un
    /// ScheduledListViewModel.
    /// -------------
    public event EventHandler<string>? TitleCommitted;

    /// -------------Inicia la edicion inline del titulo.</summary>
    public void BeginEdit()
    {
        if (!IsClosable) return;
        EditingTitle = DisplayTitle;
        IsEditing = true;
    }

    /// -------------Confirma la edicion y aplica el nuevo titulo.</summary>
    public void CommitEdit()
    {
        if (!IsEditing) return;

        var newTitle = EditingTitle?.Trim();
        if (string.IsNullOrWhiteSpace(newTitle))
            newTitle = LocalizationService.GetString("Loc.Tabs.Untitled");

        Title = newTitle;
        IsEditing = false;

        // NO tocamos IsDirty aca: el renombre se propaga al VM de contenido
        // via TitleCommitted, y ese VM marca su propio IsDirty. Si ademas
        // marcaramos el TabItemViewModel.IsDirty, quedaria pegado y mostraria
        // un "*" que nunca se limpia al guardar.
        try { TitleCommitted?.Invoke(this, newTitle); } catch { }
    }

    /// -------------Cancela la edicion sin aplicar cambios.</summary>
    public void CancelEdit()
    {
        EditingTitle = Title;
        IsEditing = false;
    }

    public void RefreshLocalizedTitle() => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
}
