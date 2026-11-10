namespace RMVoice.UI.Models;

/// <summary>
/// Elemento simple para los ComboBox de hora (00..23) y minuto (00..59).
/// </summary>
public sealed class IndexOption
{
    public int Value { get; }

    public string Label => Value.ToString("00");

    public IndexOption(int value) => Value = value;

    public override string ToString() => Label;
}
