using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del editor modal de una pista de playlist.
///
/// Trabaja sobre un CLON. Los cambios solo se aplican al original cuando
/// el usuario presiona Guardar.
/// </summary>
public sealed partial class PlaylistTrackEditorViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PlaylistTrackViewModel _original;

    /// <summary>Pista en edicion (clon). El editor bindea contra esto.</summary>
    public PlaylistTrackViewModel WorkingTrack { get; }

    public string Header => "Editar pista de playlist";

    // ───── Constructor ───────────────────────────────────────────────────

    public PlaylistTrackEditorViewModel(PlaylistTrackViewModel original)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));

        var clone = DeepClone(original.Source);
        WorkingTrack = new PlaylistTrackViewModel(clone);
    }

    // ───── Guardar ───────────────────────────────────────────────────────

    public void ApplyToOriginal()
    {
        _original.CopyFrom(WorkingTrack);
    }

    // ───── Deep clone por JSON ──────────────────────────────────────────

    private static PlaylistTrack DeepClone(PlaylistTrack source)
    {
        try
        {
            var json = JsonSerializer.Serialize(source, CloneOptions);
            var clone = JsonSerializer.Deserialize<PlaylistTrack>(json, CloneOptions);
            return clone ?? new PlaylistTrack();
        }
        catch
        {
            return new PlaylistTrack();
        }
    }
}