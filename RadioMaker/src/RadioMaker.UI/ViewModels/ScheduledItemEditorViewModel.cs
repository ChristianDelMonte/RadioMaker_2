using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RadioMaker.Core.Scheduling;

namespace RadioMaker.UI.ViewModels;

/// <summary>
/// ViewModel del editor modal de un item programado.
///
/// Trabaja sobre un CLON del item original. Los cambios solo se aplican
/// al original si el usuario presiona Guardar.
/// </summary>
public sealed partial class ScheduledItemEditorViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ScheduledItemViewModel _original;

    /// <summary>Item en edicion (clon). El editor bindea contra esto.</summary>
    public ScheduledItemViewModel WorkingItem { get; }

    /// <summary>Prioridades para el ComboBox.</summary>
    public SchedulePriority[] AvailablePriorities { get; } =
        { SchedulePriority.Low, SchedulePriority.High };

    public string Header => $"Editar item programado";

    // ───── Constructor ───────────────────────────────────────────────────

    public ScheduledItemEditorViewModel(ScheduledItemViewModel original)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));

        var clone = DeepClone(original.Source);
        WorkingItem = new ScheduledItemViewModel(clone);
    }

    // ───── Guardar ───────────────────────────────────────────────────────

    /// <summary>Aplica los cambios al item original.</summary>
    public void ApplyToOriginal()
    {
        _original.CopyFrom(WorkingItem);
    }

    // ───── Deep clone por JSON ──────────────────────────────────────────

    private static ScheduledItem DeepClone(ScheduledItem source)
    {
        try
        {
            var json = JsonSerializer.Serialize(source, CloneOptions);
            var clone = JsonSerializer.Deserialize<ScheduledItem>(json, CloneOptions);
            return clone ?? new ScheduledItem();
        }
        catch
        {
            return new ScheduledItem();
        }
    }
}