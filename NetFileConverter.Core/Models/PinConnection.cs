namespace NetFileConverter.Core.Models;

/// <summary>
/// Пин компонента, подключенный к цепи
/// </summary>
public class PinConnection
{
    public string ComponentRef { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty; // "1", "2", "A", "B"
}