using Avalonia.Controls;
using Avalonia.Interactivity;
using RadioMaker.UI.ViewModels;
using RadioMaker.UI.Views;

namespace RadioMaker.UI.Controls;

public partial class LogPanel : UserControl
{
    public LogPanel()
    {
        InitializeComponent();
    }

    private PlaybackLogViewModel? GetVm() => DataContext as PlaybackLogViewModel;

    /// -------------
    /// Abre el dialogo modal de exportacion de log.
    /// -------------
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var exportVm = new LogExportDialogViewModel(
            vm.GetAllEntries(),
            vm.GetFilteredEntries());

        var dialog = new LogExportDialog { DataContext = exportVm };

        await dialog.ShowDialog<bool>(owner);

        // `vm` ya se usa arriba (GetAllEntries/GetFilteredEntries). No hace
        // falta ningún no-op para silenciar warnings. Si en el futuro el
        // VM agrega un StatusMessage, se puede actualizar aca tras el
        // resultado del diálogo (hoy cierra silenciosamente).
    }
}