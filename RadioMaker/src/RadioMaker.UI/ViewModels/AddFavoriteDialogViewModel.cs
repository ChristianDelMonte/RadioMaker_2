using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.UI.Localization;

namespace RadioMaker.UI.ViewModels;

/// -------------
/// ViewModel del dialogo "Agregar a Favoritos".
/// Pide un nombre personalizado opcional para la carpeta.
///
/// Si el usuario deja el nombre vacio, el favorito usa el nombre de
/// la carpeta por defecto.
/// -------------
public sealed partial class AddFavoriteDialogViewModel : ViewModelBase
{
    /// -------------Ruta de la carpeta a agregar.</summary>
    public string FolderPath { get; }

    /// -------------Nombre por defecto de la carpeta (para mostrar en el dialogo).</summary>
    public string DefaultName { get; }

    /// -------------Nombre personalizado ingresado por el usuario (puede quedar vacio).</summary>
    [ObservableProperty] private string _customName = string.Empty;

    /// -------------Nombre que se usara finalmente: CustomName si esta cargado, sino DefaultName.</summary>
    public string EffectiveName =>
        !string.IsNullOrWhiteSpace(CustomName) ? CustomName.Trim() : DefaultName;

    public AddFavoriteDialogViewModel(string folderPath)
    {
        FolderPath = folderPath ?? string.Empty;
        DefaultName = ComputeDefaultName(folderPath);
    }

    private static string ComputeDefaultName(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return LocalizationService.GetString("Loc.Views.FavoriteDialog.EmptyName");

        var trimmed = folderPath!.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);

        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    partial void OnCustomNameChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveName));
    }
}
