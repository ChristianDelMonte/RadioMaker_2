namespace RadioMaker.UI.Services;

/// -------------
/// Persistencia de drafts de las pestañas de Notas.
///
/// Cada pestaña de Notas tiene un <c>DraftId</c> unico (GUID) que se usa
/// como nombre de archivo. Los drafts viven en:
///     %APPDATA%/RmsAudioPlayer/notes/{DraftId}.txt
///
/// El draft siempre refleja el estado ACTUAL del editor, haya sido guardado
/// a un archivo o no. Al restaurar el workspace, el draft tiene prioridad
/// sobre el archivo .txt guardado, porque representa la sesion mas reciente.
/// -------------
public static class NotesDraftStore
{
    /// -------------Carpeta donde viven todos los drafts.</summary>
    public static string DraftsFolder { get; } = BuildFolderPath();

    /// -------------Ruta completa del draft para un DraftId.</summary>
    public static string GetDraftPath(string draftId)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            return string.Empty;
        return Path.Combine(DraftsFolder, draftId + ".txt");
    }

    /// -------------
    /// Carga el contenido del draft. Devuelve null si no existe o si falla.
    /// -------------
    public static string? LoadDraft(string draftId)
    {
        try
        {
            var path = GetDraftPath(draftId);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return File.ReadAllText(path);
        }
        catch { return null; }
    }

    /// -------------
    /// Guarda (o sobreescribe) el draft. Acepta content null (lo trata como
    /// cadena vacia), porque en algunos flujos el Text del editor puede
    /// llegar null durante la inicializacion.
    /// -------------
    public static void SaveDraft(string draftId, string? content)
    {
        if (string.IsNullOrWhiteSpace(draftId)) return;

        try
        {
            Directory.CreateDirectory(DraftsFolder);
            File.WriteAllText(GetDraftPath(draftId), content ?? string.Empty);
        }
        catch { }
    }

    /// -------------Borra el draft si existe.</summary>
    public static void DeleteDraft(string draftId)
    {
        try
        {
            var path = GetDraftPath(draftId);
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string BuildFolderPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(root, "RadioMaker", "notes");
    }
}