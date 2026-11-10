namespace RMVoice.UI.Services;

public interface IDialogService
{
    Task<string?> PickFileAsync(string title, string[] patterns);
    Task<string?> SaveFileAsync(string title, string suggestedFileName, string[] patterns);
}
