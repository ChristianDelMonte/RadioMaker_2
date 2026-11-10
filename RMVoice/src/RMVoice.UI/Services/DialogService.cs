using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RMVoice.UI.Services;

/// <summary>
/// Implementación de IDialogService usando el StorageProvider de Avalonia 11.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner;

    public async Task<string?> PickFileAsync(string title, string[] patterns)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(title) { Patterns = patterns },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string[] patterns)
    {
        var extension = patterns.FirstOrDefault()?.TrimStart('*', '.');

        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(title) { Patterns = patterns }
            }
        });

        return file?.Path.LocalPath;
    }
}
