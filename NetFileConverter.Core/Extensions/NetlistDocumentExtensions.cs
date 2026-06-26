using NetFileConverter.Core.Models;

namespace NetFileConverter.Core.Extensions;

public static class NetlistDocumentExtensions
{
    public static Component? FindComponent(this NetlistDocument doc, string refName)
        => doc.Components.FirstOrDefault(c =>
            c.Ref.Equals(refName, StringComparison.OrdinalIgnoreCase));

    public static Net? FindNet(this NetlistDocument doc, string netName)
        => doc.Nets.FirstOrDefault(n =>
            n.Name.Equals(netName, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<PinConnection> GetPinsForComponent(this NetlistDocument doc, string refName)
        => doc.Nets.SelectMany(n => n.Pins)
            .Where(p => p.ComponentRef.Equals(refName, StringComparison.OrdinalIgnoreCase));
}