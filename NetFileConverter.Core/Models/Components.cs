namespace NetFileConverter.Core.Models;

/// <summary>
/// Компонент на схеме
/// </summary>
public class Component
{
    public string Ref { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Footprint { get; set; } = string.Empty;
    public Dictionary<string, string>? Metadata { get; set; }
}