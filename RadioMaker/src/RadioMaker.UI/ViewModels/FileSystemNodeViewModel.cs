using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// Nodo del arbol de directorios del File Manager.
///
/// Carga lazy: los hijos se enumeran la primera vez que el nodo se expande
/// o cuando alguien llama explicitamente a EnsureChildrenLoadedAsync.
/// La tarea de carga se cachea para que multiples llamadas concurrentes
/// compartan el mismo trabajo (y para poder esperarla).
/// -------------
public sealed partial class FileSystemNodeViewModel : ViewModelBase
{
    /// -------------Ruta absoluta del directorio representado por este nodo.</summary>
    public string FullPath { get; }

    /// -------------Nombre visible (solo el nombre de la carpeta, no la ruta completa).</summary>
    public string DisplayName { get; }

    /// -------------Hijos del nodo. Se cargan lazy.</summary>
    public ObservableCollection<FileSystemNodeViewModel> Children { get; } = new();

    /// -------------Indica si el nodo esta expandido en el TreeView.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// -------------Indica si el nodo esta seleccionado en el TreeView.</summary>
    [ObservableProperty] private bool _isSelected;
    
    /// -------------
    /// Indica si esta carpeta esta en la lista de favoritos del usuario.
    /// Se actualiza cuando cambia el registry de favoritos.
    /// -------------
    [ObservableProperty] private bool _isFavorite;

    /// -------------Tarea de carga de hijos, cacheada. Null hasta la primera llamada.</summary>
    private Task? _loadTask;

    /// -------------Gate para evitar race conditions en la creacion de _loadTask.</summary>
    private readonly object _loadGate = new();

    /// -------------Icono (por ahora string, mas adelante podria ser un Path de vector).</summary>
    public string Icon { get; }

    /// -------------Evento para notificar al padre que se selecciono un directorio.</summary>
    public event EventHandler<FileSystemNodeViewModel>? Selected;

    // ───── Constructor ───────────────────────────────────────────────────

    public FileSystemNodeViewModel(string fullPath, string displayName, string icon = "📁")
    {
        FullPath = fullPath;
        DisplayName = displayName;
        Icon = icon;
    }

    // ───── Carga lazy (awaitable) ────────────────────────────────────────

    /// -------------
    /// Garantiza que los hijos esten cargados y devuelve el Task para
    /// poder esperarlo. Llamadas concurrentes comparten el mismo Task.
    /// -------------
    public Task EnsureChildrenLoadedAsync()
    {
        lock (_loadGate)
        {
            _loadTask ??= LoadChildrenInternalAsync();
            return _loadTask;
        }
    }

    /// -------------
    /// Enumeracion real de subdirectorios. Corre en background la parte
    /// pesada (I/O) y agrega los hijos en el hilo original.
    /// -------------
    private async Task LoadChildrenInternalAsync()
    {
        List<string> dirs;

        try
        {
            dirs = await Task.Run(() =>
            {
                try
                {
                    return Directory.EnumerateDirectories(FullPath)
                        .OrderBy(p => Path.GetFileName(p))
                        .ToList();
                }
                catch
                {
                    return new List<string>();
                }
            });
        }
        catch
        {
            return;
        }

        foreach (var dir in dirs)
        {
            try
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                    name = dir;

                Children.Add(new FileSystemNodeViewModel(dir, name));
            }
            catch { }
        }
    }

    /// -------------
    /// Fuerza la recarga de hijos (por ejemplo si cambio el contenido).
    /// La proxima llamada a EnsureChildrenLoadedAsync vuelve a enumerar.
    /// -------------
    public void Refresh()
    {
        lock (_loadGate)
        {
            _loadTask = null;
        }
        Children.Clear();
    }

    // ───── Hooks ─────────────────────────────────────────────────────────

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            Selected?.Invoke(this, this);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        // Cuando el usuario expande el nodo manualmente, disparar la carga
        // si todavia no arranco.
        if (value)
            _ = EnsureChildrenLoadedAsync();
    }

    // ───── Factory para raices del sistema ───────────────────────────────

    /// -------------
    /// Crea los nodos raiz del sistema de archivos.
    /// En Windows: las unidades locales y montajes (C:\, D:\, etc).
    /// En Linux/macOS: la raiz "/" mas los volumenes tipicos.
    /// -------------
    public static List<FileSystemNodeViewModel> CreateRootNodes()
    {
        var roots = new List<FileSystemNodeViewModel>();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady) continue;

                        string label = string.IsNullOrEmpty(drive.VolumeLabel)
                            ? drive.Name
                            : $"{drive.Name} ({drive.VolumeLabel})";

                        string icon = drive.DriveType switch
                        {
                            DriveType.Fixed => "💾",
                            DriveType.Removable => "💿",
                            DriveType.Network => "🌐",
                            DriveType.CDRom => "📀",
                            _ => "💾"
                        };

                        roots.Add(new FileSystemNodeViewModel(drive.Name, label, icon));
                    }
                    catch { }
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                roots.Add(new FileSystemNodeViewModel("/", "Macintosh HD", "💾"));

                var volumes = "/Volumes";
                if (Directory.Exists(volumes))
                {
                    foreach (var v in Directory.EnumerateDirectories(volumes))
                    {
                        try
                        {
                            var name = Path.GetFileName(v);
                            if (!string.IsNullOrEmpty(name))
                                roots.Add(new FileSystemNodeViewModel(v, name, "💿"));
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // Linux y otros Unix
                roots.Add(new FileSystemNodeViewModel("/", "Sistema de archivos", "💾"));

                // Puntos de montaje comunes.
                var mountDirs = new[] { "/media", "/mnt" };
                foreach (var md in mountDirs)
                {
                    if (!Directory.Exists(md)) continue;

                    foreach (var v in Directory.EnumerateDirectories(md))
                    {
                        try
                        {
                            var name = Path.GetFileName(v);
                            if (!string.IsNullOrEmpty(name))
                                roots.Add(new FileSystemNodeViewModel(v, name, "💿"));
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        return roots;
    }
}