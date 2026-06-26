using System.Text;
using System.Text.RegularExpressions;
using NetFileConverter.Core.Models;
using NetFileConverter.Core.Interfaces;

namespace NetFileConverter.Infrastructure.Parsers;

/// <summary>
/// Парсер нетлистов KiCad (формат S-выражений)
/// </summary>
public class KiCadParser : INetlistParser
{
    private readonly Dictionary<string, ComponentInfo> _components = new();
    private readonly SortedDictionary<string, Dictionary<string, SortedSet<string>>> _nets = new();
    private bool _hasWarnings;

    public NetlistDocument Parse(string filePath)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var tree = SExpressionParser.Parse(content);
        
        if (tree == null || tree.Count == 0 || tree[0] is not string rootTag || rootTag != "export")
        {
            throw new InvalidDataException($"Файл {Path.GetFileName(filePath)} не является корректным KiCad-нетлистом.");
        }

        // Очищаем состояние перед парсингом
        _components.Clear();
        _nets.Clear();
        _hasWarnings = false;

        for (int i = 1; i < tree.Count; i++)
        {
            if (tree[i] is not List<object> block) continue;

            if (IsTag(block, "components"))
            {
                ParseComponents(block);
            }
            else if (IsTag(block, "nets"))
            {
                ParseNets(block);
            }
        }

        // Строим NetlistDocument из собранных данных
        return BuildDocument(filePath);
    }

    private void ParseComponents(List<object> block)
    {
        for (int j = 1; j < block.Count; j++)
        {
            if (block[j] is List<object> comp && IsTag(comp, "comp"))
            {
                string refName = GetValueForTag(comp, "ref");
                string value = GetValueForTag(comp, "value");
                string footprint = GetValueForTag(comp, "footprint");

                if (footprint.Contains(":"))
                    footprint = footprint.Substring(footprint.LastIndexOf(':') + 1);

                string baseRef = CleanRef(refName);
                if (!_components.ContainsKey(baseRef))
                {
                    bool isMissing = string.IsNullOrWhiteSpace(footprint) || footprint == "~";
                    if (isMissing) _hasWarnings = true;

                    _components[baseRef] = new ComponentInfo
                    {
                        Value = value,
                        Footprint = isMissing ? "!!! НЕТ ФУТПРИНТА !!!" : footprint,
                        IsMissingFp = isMissing
                    };
                }
            }
        }
    }

    private void ParseNets(List<object> block)
    {
        for (int j = 1; j < block.Count; j++)
        {
            if (block[j] is List<object> net && IsTag(net, "net"))
            {
                string netName = GetValueForTag(net, "name");

                // Фильтруем unconnected и пустые
                if (string.IsNullOrWhiteSpace(netName) ||
                    netName.IndexOf("unconnected", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!_nets.ContainsKey(netName))
                    _nets[netName] = new Dictionary<string, SortedSet<string>>();

                foreach (var item in net)
                {
                    if (item is List<object> node && IsTag(node, "node"))
                    {
                        string nRef = GetValueForTag(node, "ref");
                        string nPin = GetValueForTag(node, "pin");
                        string baseRef = CleanRef(nRef);

                        if (!_nets[netName].ContainsKey(baseRef))
                            _nets[netName][baseRef] = new SortedSet<string>(new PinComparer());
                        _nets[netName][baseRef].Add(nPin);
                    }
                }
            }
        }
    }

    private NetlistDocument BuildDocument(string filePath)
    {
        var doc = new NetlistDocument
        {
            SourceFileName = Path.GetFileName(filePath),
            Format = "KiCad",
            ParsedAt = DateTime.UtcNow,
            Components = _components.Select(kvp => new Component
            {
                Ref = kvp.Key,
                Value = kvp.Value.Value,
                Footprint = kvp.Value.Footprint,
                Metadata = kvp.Value.IsMissingFp 
                    ? new Dictionary<string, string> { ["HasWarning"] = "true" } 
                    : null
            }).ToList(),
            Metadata = _hasWarnings 
                ? new Dictionary<string, string> { ["HasWarnings"] = "true" }
                : null
        };

        // Добавляем цепи
        foreach (var netKvp in _nets)
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

    // ─── Вспомогательные методы (скопированы из старого кода) ───

    private string CleanRef(string refName)
    {
        var match = Regex.Match(refName, @"^([A-Z]+\d+)[A-Z]?$");
        return match.Success ? match.Groups[1].Value : refName;
    }

    private bool IsTag(object item, string tag)
    {
        return item is List<object> list && list.Count > 0 && list[0] is string s && s == tag;
    }

    private string GetValueForTag(List<object> block, string tag)
    {
        foreach (var subItem in block)
        {
            if (IsTag(subItem, tag))
            {
                var list = (List<object>)subItem;
                if (list.Count > 1 && list[1] is string val) return val;
            }
        }
        return "~";
    }

    // ─── Внутренние модели для парсинга ───

    private class ComponentInfo
    {
        public string Value { get; set; } = "~";
        public string Footprint { get; set; } = "~";
        public bool IsMissingFp { get; set; }
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