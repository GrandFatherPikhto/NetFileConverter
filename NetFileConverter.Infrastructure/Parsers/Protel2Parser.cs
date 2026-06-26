using System.Text;

using System.Text.RegularExpressions;
using NetFileConverter.Core.Models;
using NetFileConverter.Core.Interfaces;

namespace NetFileConverter.Infrastructure.Parsers;

/// <summary>
/// Парсер нетлистов Protel Netlist 2.0 (Altium Designer)
/// </summary>
public class Protel2Parser : INetlistParser
{
    public NetlistDocument Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath, DetectEncoding(filePath));
        var components = new Dictionary<string, Component>();
        var nets = new SortedDictionary<string, Dictionary<string, SortedSet<string>>>();
        bool hasWarnings = false;

        int i = 0;
        // Пропускаем заголовок
        if (lines.Length > 0 && lines[0].StartsWith("PROTEL NETLIST", StringComparison.OrdinalIgnoreCase))
            i = 1;

        while (i < lines.Length)
        {
            string raw = lines[i].Trim();

            // Блок компонента
            if (raw == "[")
            {
                i++;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? lastKey = null;

                while (i < lines.Length && lines[i].Trim() != "]")
                {
                    string line = lines[i].Trim();
                    if (line == "*" || line == "")
                    {
                        lastKey = null;
                        i++;
                        continue;
                    }

                    if (lastKey != null)
                    {
                        if (!fields.ContainsKey(lastKey))
                            fields[lastKey] = line;
                        lastKey = null;
                    }
                    else
                    {
                        lastKey = line;
                    }
                    i++;
                }
                i++; // пропускаем "]"

                if (fields.TryGetValue("DESIGNATOR", out string? designator) && !string.IsNullOrWhiteSpace(designator))
                {
                    string refName = designator.Trim();
                    string value = fields.TryGetValue("PARTTYPE", out string? pt) && !string.IsNullOrWhiteSpace(pt)
                        ? pt.Trim() : "~";
                    string footprint = fields.TryGetValue("FOOTPRINT", out string? fp) && !string.IsNullOrWhiteSpace(fp)
                        ? fp.Trim() : "";

                    bool isMissing = string.IsNullOrWhiteSpace(footprint);
                    if (isMissing) hasWarnings = true;

                    if (!components.ContainsKey(refName))
                    {
                        components[refName] = new Component
                        {
                            Ref = refName,
                            Value = value,
                            Footprint = isMissing ? "!!! НЕТ ФУТПРИНТА !!!" : footprint,
                            Metadata = isMissing ? new Dictionary<string, string> { ["HasWarning"] = "true" } : null
                        };
                    }
                }
                continue;
            }

            // Блок цепи
            if (raw == "(")
            {
                i++;
                if (i >= lines.Length) break;
                string netName = lines[i].Trim();
                i++;

                bool skip = string.IsNullOrWhiteSpace(netName)
                            || netName.IndexOf("unconnected", StringComparison.OrdinalIgnoreCase) >= 0;

                var netNodes = skip
                    ? null
                    : nets.TryGetValue(netName, out var existing)
                        ? existing
                        : nets[netName] = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

                while (i < lines.Length && lines[i].Trim() != ")")
                {
                    string nodeLine = lines[i].Trim();
                    i++;

                    if (string.IsNullOrEmpty(nodeLine) || skip) continue;

                    // Формат: "REF-PIN Value- Passive"
                    int dashIdx = nodeLine.IndexOf('-');
                    if (dashIdx <= 0) continue;

                    string refPart = nodeLine.Substring(0, dashIdx).Trim();
                    string rest = nodeLine.Substring(dashIdx + 1);
                    int spaceIdx = rest.IndexOf(' ');
                    string pinPart = spaceIdx > 0 ? rest.Substring(0, spaceIdx).Trim() : rest.Trim();

                    if (string.IsNullOrEmpty(refPart) || string.IsNullOrEmpty(pinPart)) continue;

                    if (!netNodes!.ContainsKey(refPart))
                        netNodes[refPart] = new SortedSet<string>(new PinComparer());
                    netNodes[refPart].Add(pinPart);
                }
                i++; // пропускаем ")"
                continue;
            }

            i++;
        }

        // Строим NetlistDocument
        var doc = new NetlistDocument
        {
            SourceFileName = Path.GetFileName(filePath),
            Format = "Protel2",
            ParsedAt = DateTime.UtcNow,
            Components = components.Values.ToList(),
            Metadata = hasWarnings ? new Dictionary<string, string> { ["HasWarnings"] = "true" } : null
        };

        // Добавляем цепи
        foreach (var netKvp in nets)
        {
            var net = new Net
            {
                Name = netKvp.Key,
                Pins = netKvp.Value
                    .SelectMany(compKvp => compKvp.Value.Select(pin => new PinConnection
                    {
                        ComponentRef = compKvp.Key,
                        Pin = pin
                    }))
                    .ToList()
            };
            doc.Nets.Add(net);
        }

        return doc;
    }

    /// <summary>
    /// Определяем кодировку файла по BOM или возвращаем Windows-1252
    /// </summary>
    private static Encoding DetectEncoding(string path)
    {
        byte[] bom = new byte[4];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        int read = fs.Read(bom, 0, 4);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        try { return Encoding.GetEncoding(1252); }
        catch { return Encoding.Latin1; }
    }

    private class PinComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            bool xIsNum = int.TryParse(x, out int xNum);
            bool yIsNum = int.TryParse(y, out int yNum);

            if (xIsNum && yIsNum) return xNum.CompareTo(yNum);
            if (xIsNum) return -1;
            if (yIsNum) return 1;
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}