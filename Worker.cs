using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace FolderWatcher.Service;

// Модель для элемента из конфигурации
public class SourceDirectory
{
    public string Path { get; set; } = "";
    public string Format { get; set; } = "Protel2";
}

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly List<(FileSystemWatcher Watcher, string Format)> _watchers = new();

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Сервис мониторинга запущен.");
        StartWatchers();

        // Подписываемся на изменение конфигурации
        ChangeToken.OnChange(
            () => _configuration.GetReloadToken(),
            () =>
            {
                _logger.LogInformation("Конфигурация изменена, перезапуск наблюдателей...");
                StartWatchers();
            });

        return Task.CompletedTask;
    }

    private void StartWatchers()
    {
        // Останавливаем старые наблюдатели
        foreach (var (w, _) in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();

        // Получаем список папок из конфигурации (поддерживаем оба формата)
        var dirs = GetSourceDirectories();
        if (dirs == null || dirs.Count == 0)
        {
            _logger.LogWarning("Нет отслеживаемых папок в конфигурации.");
            return;
        }

        foreach (var dir in dirs)
        {
            string folder = dir.Path;
            string format = NormalizeFormat(dir.Format);

            // Если путь указывает на файл, берём его директорию
            if (File.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder)!;
                _logger.LogInformation($"Путь '{dir.Path}' является файлом, наблюдаем папку '{folder}'");
            }

            if (!Directory.Exists(folder))
            {
                _logger.LogWarning($"Папка не существует: {folder}");
                continue;
            }

            // Первичное сканирование существующих .net файлов
            InitialScan(folder, format);

            // Настраиваем FileSystemWatcher
            var watcher = new FileSystemWatcher(folder, "*.*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += (s, e) => ProcessFile(e.FullPath, format);
            watcher.Created += (s, e) => ProcessFile(e.FullPath, format);
            watcher.Renamed += (s, e) => ProcessFile(e.FullPath, format);

            _watchers.Add((watcher, format));
            _logger.LogInformation($"Мониторинг запущен для: {folder} [формат: {format}]");
        }
    }

    /// <summary>Читает SourceDirectories из конфигурации (поддерживает старый и новый формат)</summary>
    private List<SourceDirectory> GetSourceDirectories()
    {
        var section = _configuration.GetSection("WatcherSettings:SourceDirectories");
        var result = new List<SourceDirectory>();

        // Пытаемся прочитать как массив объектов
        var typed = section.Get<List<SourceDirectory>>();
        if (typed != null && typed.Count > 0)
            return typed;

        // Иначе пробуем прочитать как массив строк (старая версия)
        var strings = section.Get<string[]>();
        if (strings != null)
        {
            foreach (var s in strings)
                result.Add(new SourceDirectory { Path = s, Format = "Protel2" });
        }

        return result;
    }

    private string NormalizeFormat(string format)
    {
        if (string.IsNullOrEmpty(format)) return "Protel2";
        if (format.Contains("KiCad", StringComparison.OrdinalIgnoreCase))
            return "KiCad";
        return "Protel2"; // всё остальное, включая "Protel2Netlist"
    }

    private void InitialScan(string path, string format)
    {
        _logger.LogInformation($"Сканирование папки {path} (формат {format}) на наличие .net файлов...");
        var files = Directory.EnumerateFiles(path, "*.*")
                    .Where(f => f.EndsWith(".net", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            _logger.LogInformation($"Найден существующий файл: {Path.GetFileName(file)}");
            ProcessFile(file, format);
        }
    }

    private void ProcessFile(string filePath, string format)
    {
        if (!filePath.EndsWith(".net", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            // Даём файлу освободиться
            Thread.Sleep(500);

            string directory = Path.GetDirectoryName(filePath)!;
            string outFolder = Path.Combine(directory, "out");
            if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);

            string baseName = Path.GetFileNameWithoutExtension(filePath);

            // Копируем исходник
            string origPath = Path.Combine(outFolder, baseName + "_orig.txt");
            File.Copy(filePath, origPath, overwrite: true);

            var lines = File.ReadAllLines(filePath);
            ParseResult parseResult;

            if (format == "KiCad")
                parseResult = ParseKiCadNetlist(lines);
            else
                parseResult = ParseProtel2Netlist(lines);

            WriteNetFile(parseResult.Nets, outFolder, baseName);
            WriteBomFile(parseResult.Components, outFolder, baseName);
            WriteDotFile(parseResult.Components, parseResult.Nets, outFolder, baseName);

            _logger.LogInformation($"Обработан: {Path.GetFileName(filePath)} -> out/{baseName}_*.txt, *.dot");
            Program.ShowNotification("Файл успешно обработан",
                $"Конвертация файла {Path.GetFileName(filePath)} завершена.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при обработке {filePath}");
            Program.ShowNotification("Ошибка конвертации", ex.Message, ToolTipIcon.Error);
        }
    }

    // ========== Парсер Protel2 (без изменений) ==========
    private ParseResult ParseProtel2Netlist(string[] lines)
    {
        var result = new ParseResult();
        bool inNetSection = false, inComponentSection = false;
        NetInfo? currentNet = null;
        ComponentInfo? currentComponent = null;
        string? expectedField = null;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line == "[" && !inNetSection)
            {
                inComponentSection = true;
                currentComponent = new ComponentInfo();
                expectedField = null;
                continue;
            }
            if (line == "]" && inComponentSection)
            {
                inComponentSection = false;
                if (currentComponent != null && !string.IsNullOrEmpty(currentComponent.Designator))
                    result.Components.Add(currentComponent);
                currentComponent = null;
                continue;
            }
            if (line == "(")
            {
                inNetSection = true;
                currentNet = new NetInfo();
                continue;
            }
            if (line == ")")
            {
                inNetSection = false;
                if (currentNet != null && !string.IsNullOrEmpty(currentNet.NetName) && currentNet.Pins.Count > 0)
                    result.Nets.Add(currentNet);
                currentNet = null;
                continue;
            }

            if (inComponentSection)
                ParseComponentLine(line, currentComponent!, ref expectedField);
            else if (inNetSection)
                ParseNetLine(line, currentNet!);
        }
        return result;
    }

    private void ParseComponentLine(string line, ComponentInfo component, ref string? expectedField)
    {
        if (expectedField != null)
        {
            switch (expectedField)
            {
                case "DESIGNATOR": component.Designator = line; break;
                case "PARTTYPE": component.PartType = line; break;
                case "Comment": component.Comment = line; break;
            }
            expectedField = null;
            return;
        }
        if (line.Equals("DESIGNATOR", StringComparison.OrdinalIgnoreCase)) expectedField = "DESIGNATOR";
        else if (line.Equals("PARTTYPE", StringComparison.OrdinalIgnoreCase)) expectedField = "PARTTYPE";
        else if (line.Equals("Comment", StringComparison.OrdinalIgnoreCase)) expectedField = "Comment";
    }

    private void ParseNetLine(string line, NetInfo net)
    {
        if (string.IsNullOrEmpty(net.NetName))
        {
            net.NetName = line;
            return;
        }
        var match = Regex.Match(line, @"^(?<pin>[A-Za-z0-9]+-\d+)");
        if (match.Success)
            net.Pins.Add(match.Groups["pin"].Value);
    }

    // ========== Парсер KiCad (s-expression) ==========
    private ParseResult ParseKiCadNetlist(string[] lines)
    {
        var result = new ParseResult();
        string fullText = string.Join(" ", lines).Replace("\r", " ").Replace("\n", " ");
        var tokens = TokenizeSexpr(fullText);
        int idx = 0;
        var root = ParseSexpr(tokens, ref idx);

        // Компоненты
        var compsNode = FindChild(root, "components");
        if (compsNode != null)
        {
            foreach (var compNode in FindAllChildren(compsNode, "comp"))
            {
                var comp = new ComponentInfo();
                comp.Designator = GetStringChild(compNode, "ref") ?? "";
                comp.PartType = GetStringChild(compNode, "value") ?? "?";
                var fieldsNode = FindChild(compNode, "fields");
                if (fieldsNode != null)
                {
                    foreach (var fieldNode in FindAllChildren(fieldsNode, "field"))
                    {
                        var nameNode = FindChild(fieldNode, "name");
                        if (nameNode != null && nameNode.Children.Count > 0 && nameNode.Children[0].StringValue == "Comment")
                        {
                            comp.Comment = GetStringValue(fieldNode) ?? "";
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(comp.Designator))
                    result.Components.Add(comp);
            }
        }

        // Цепи
        var netsNode = FindChild(root, "nets");
        if (netsNode != null)
        {
            foreach (var netNode in FindAllChildren(netsNode, "net"))
            {
                var net = new NetInfo();
                net.NetName = GetStringChild(netNode, "name") ?? "";
                foreach (var node in FindAllChildren(netNode, "node"))
                {
                    string refDes = GetStringChild(node, "ref") ?? "";
                    string pin = GetStringChild(node, "pin") ?? "";
                    if (!string.IsNullOrEmpty(refDes) && !string.IsNullOrEmpty(pin))
                        net.Pins.Add($"{refDes}-{pin}");
                }
                if (!string.IsNullOrEmpty(net.NetName) && net.Pins.Count > 0)
                    result.Nets.Add(net);
            }
        }
        return result;
    }

    // Вспомогательные методы для s-expression (KiCad)
    private class SexprNode
    {
        public string Name { get; set; } = "";
        public List<SexprNode> Children { get; } = new();
        public string? StringValue { get; set; }
    }

    private List<string> TokenizeSexpr(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inString = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (inString && i > 0 && text[i - 1] == '\\') sb.Append(c);
                else
                {
                    if (inString)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    inString = !inString;
                    if (!inString) continue;
                }
            }
            if (inString)
            {
                sb.Append(c);
                continue;
            }
            if (c == '(' || c == ')')
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                tokens.Add(c.ToString());
            }
            else if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private SexprNode ParseSexpr(List<string> tokens, ref int idx)
    {
        var node = new SexprNode();
        if (tokens[idx] == "(")
        {
            idx++;
            if (idx < tokens.Count && tokens[idx] != "(" && tokens[idx] != ")")
            {
                node.Name = tokens[idx];
                idx++;
            }
            while (idx < tokens.Count && tokens[idx] != ")")
            {
                if (tokens[idx] == "(")
                    node.Children.Add(ParseSexpr(tokens, ref idx));
                else
                {
                    var child = new SexprNode { StringValue = tokens[idx] };
                    node.Children.Add(child);
                    idx++;
                }
            }
            if (idx < tokens.Count && tokens[idx] == ")") idx++;
        }
        else
        {
            node.StringValue = tokens[idx];
            idx++;
        }
        return node;
    }

    private SexprNode? FindChild(SexprNode node, string name) =>
        node.Children.FirstOrDefault(c => c.Name == name);

    private IEnumerable<SexprNode> FindAllChildren(SexprNode node, string name) =>
        node.Children.Where(c => c.Name == name);

    private string? GetStringChild(SexprNode node, string childName)
    {
        var child = FindChild(node, childName);
        if (child != null)
            return GetStringValue(child); // используем общий метод с очисткой кавычек
        return null;
    }

    private string? GetStringValue(SexprNode node)
    {
        string? raw = null;
        if (node.StringValue != null)
            raw = node.StringValue;
        else if (node.Children.Count > 0 && node.Children[0].StringValue != null)
            raw = node.Children[0].StringValue;

        if (raw == null) return null;
        
        // Удаляем обрамляющие двойные кавычки, если они есть
        if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
            raw = raw.Substring(1, raw.Length - 2);
        
        return raw;
    }

    // ========== Запись выходных файлов ==========
    private void WriteNetFile(List<NetInfo> nets, string folder, string baseName)
    {
        var sb = new StringBuilder();
        foreach (var net in nets)
            sb.AppendLine($"{net.NetName}: {string.Join(", ", net.Pins)}");
        File.WriteAllText(Path.Combine(folder, baseName + "_net.txt"), sb.ToString(), Encoding.UTF8);
    }

    private void WriteBomFile(List<ComponentInfo> components, string folder, string baseName)
    {
        var sb = new StringBuilder();
        foreach (var comp in components)
        {
            string comment = string.IsNullOrEmpty(comp.Comment) ? "" : $" ({comp.Comment})";
            sb.AppendLine($"{comp.Designator}: {comp.PartType}{comment}");
        }
        File.WriteAllText(Path.Combine(folder, baseName + "_bom.txt"), sb.ToString(), Encoding.UTF8);
    }

    private void WriteDotFile(List<ComponentInfo> components, List<NetInfo> nets, string folder, string baseName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph Netlist {");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    node [shape=box, style=filled, fillcolor=lightyellow];");
        foreach (var comp in components)
            sb.AppendLine($"    \"{comp.Designator}\" [label=\"{comp.Designator}\\n({comp.PartType})\"];");

        sb.AppendLine();
        sb.AppendLine("    node [shape=ellipse, style=filled, fillcolor=lightblue];");
        foreach (var net in nets)
        {
            string netNodeId = $"net_{net.NetName}";
            sb.AppendLine($"    \"{netNodeId}\" [label=\"{net.NetName}\"];");

            var compPins = new Dictionary<string, List<string>>();
            foreach (var pin in net.Pins)
            {
                int dash = pin.LastIndexOf('-');
                if (dash <= 0) continue;
                string des = pin.Substring(0, dash);
                string num = pin.Substring(dash + 1);
                if (!compPins.ContainsKey(des)) compPins[des] = new List<string>();
                compPins[des].Add(num);
            }
            foreach (var kv in compPins)
            {
                string label = kv.Value.Count > 1 ? $" [label=\"{string.Join(",", kv.Value)}\"]" : "";
                sb.AppendLine($"    \"{kv.Key}\" -- \"{netNodeId}\"{label};");
            }
        }
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(folder, baseName + "_net.dot"), sb.ToString(), Encoding.UTF8);
    }

    public override void Dispose()
    {
        foreach (var (w, _) in _watchers) w.Dispose();
        base.Dispose();
    }

    // Вспомогательные внутренние классы
    private class ParseResult
    {
        public List<ComponentInfo> Components { get; } = new();
        public List<NetInfo> Nets { get; } = new();
    }

    private class ComponentInfo
    {
        public string Designator { get; set; } = "";
        public string PartType { get; set; } = "?";
        public string Comment { get; set; } = "";
    }

    private class NetInfo
    {
        public string NetName { get; set; } = "";
        public List<string> Pins { get; } = new();
    }
}