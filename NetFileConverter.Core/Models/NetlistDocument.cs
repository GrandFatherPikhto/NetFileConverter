namespace NetFileConverter.Core.Models;

/// <summary>
/// Корневой документ нетлиста
/// </summary>
public class NetlistDocument
{
    public string SourceFileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty; // "Protel2", "KiCad"
    public DateTime ParsedAt { get; set; } = DateTime.UtcNow;
    public List<Component> Components { get; set; } = new();
    public List<Net> Nets { get; set; } = new();
    public Dictionary<string, string>? Metadata { get; set; }
}