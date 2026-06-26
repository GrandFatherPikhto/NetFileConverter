namespace NetFileConverter.Core.Models;

/// <summary>
/// Цепь (сеть)
/// </summary>
public class Net
{
    public string Name { get; set; } = string.Empty;
    public List<PinConnection> Pins { get; set; } = new();
}