using System.Text;

using Microsoft.Extensions.Primitives;
namespace FolderWatcher.Service;
using System.Text.RegularExpressions; // Добавьте в начало файла


public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly List<FileSystemWatcher> _watchers = new();

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Сервис мониторинга запущен.");
        
        StartWatchers();

        // Подписываемся на изменение самого конфига appsettings.json
        ChangeToken.OnChange(
            () => _configuration.GetReloadToken(),
            () => {
                _logger.LogInformation("Конфигурация изменена, перезапуск наблюдателей...");
                StartWatchers();
            });

        return Task.CompletedTask;
    }

    private void StartWatchers()
    {
        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
        _watchers.Clear();

        var paths = _configuration.GetSection("WatcherSettings:SourceDirectories").Get<string[]>();
        if (paths == null) return;

        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;

            // 1. Сначала обрабатываем то, что уже лежит в папке
            InitialScan(path);

            // 2. Затем настраиваем слежку за новыми изменениями
            var watcher = new FileSystemWatcher(path, "*.*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };

            watcher.Changed += (s, e) => ProcessFile(e.FullPath);
            watcher.Created += (s, e) => ProcessFile(e.FullPath);
            watcher.Renamed += (s, e) => ProcessFile(e.FullPath);
            
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
            _logger.LogInformation($"Мониторинг запущен для: {path}");
        }
    }

    // Новый метод для обработки существующих файлов
    private void InitialScan(string path)
    {
        _logger.LogInformation($"Сканирование папки на наличие существующих файлов: {path}");
        
        // Получаем все файлы, заканчивающиеся на .net (любого регистра)
        var existingFiles = Directory.EnumerateFiles(path, "*.*")
                            .Where(f => f.EndsWith(".net", StringComparison.OrdinalIgnoreCase));

        foreach (var file in existingFiles)
        {
            _logger.LogInformation($"Найден существующий файл: {Path.GetFileName(file)}");
            ProcessFile(file);
        }
}    


// =========== Вспомогательный класс для хранения промежуточных данных ===========
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

// =========== Основной метод обработки файла ===========
private void ProcessFile(string filePath)
{
    if (!filePath.EndsWith(".net", StringComparison.OrdinalIgnoreCase)) return;

    try
    {
        // Небольшая задержка, чтобы файл освободился
        Thread.Sleep(500);

        string directory = Path.GetDirectoryName(filePath)!;
        string outFolder = Path.Combine(directory, "out");
        if (!Directory.Exists(outFolder)) Directory.CreateDirectory(outFolder);

        string baseName = Path.GetFileNameWithoutExtension(filePath);

        // === Копируем исходный файл как <name>_orig.txt ===
        string origFilePath = Path.Combine(outFolder, baseName + "_orig.txt");
        File.Copy(filePath, origFilePath, overwrite: true);
        // ===================================================

        var lines = File.ReadAllLines(filePath);
        var parseResult = ParseLines(lines);

        WriteNetFile(parseResult.Nets, outFolder, baseName);
        WriteBomFile(parseResult.Components, outFolder, baseName);
        WriteDotFile(parseResult.Components, parseResult.Nets, outFolder, baseName);   // если ещё не добавлен – см. полную версию

        _logger.LogInformation($"Обработан: {Path.GetFileName(filePath)} -> out/{baseName}_net.txt + out/{baseName}_bom.txt + out/{baseName}_net.dot + out/{baseName}_orig.txt");
    }
    catch (Exception ex)
    {
        _logger.LogError($"Ошибка при обработке {filePath}: {ex.Message}");
    }
}

// =========== Парсинг всех строк нетлиста ===========
private ParseResult ParseLines(string[] lines)
{
    var result = new ParseResult();

    bool inNetSection = false;
    bool inComponentSection = false;

    NetInfo? currentNet = null;
    ComponentInfo? currentComponent = null;
    string? expectedField = null;

    foreach (var rawLine in lines)
    {
        string line = rawLine.Trim();
        if (string.IsNullOrEmpty(line)) continue;

        // Переходы между секциями
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
            {
                result.Components.Add(currentComponent);
            }
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
            {
                result.Nets.Add(currentNet);
            }
            currentNet = null;
            continue;
        }

        // Обработка содержимого секций
        if (inComponentSection)
        {
            ParseComponentLine(line, currentComponent!, ref expectedField);
        }
        else if (inNetSection)
        {
            ParseNetLine(line, currentNet!);
        }
    }

    return result;
}

    // =========== Разбор строки внутри блока компонента ===========
    private void ParseComponentLine(string line, ComponentInfo component, ref string? expectedField)
    {
        // Если ожидаем значение для предыдущего ключа
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

        // Проверяем, является ли строка известным ключом
        if (line.Equals("DESIGNATOR", StringComparison.OrdinalIgnoreCase))
            expectedField = "DESIGNATOR";
        else if (line.Equals("PARTTYPE", StringComparison.OrdinalIgnoreCase))
            expectedField = "PARTTYPE";
        else if (line.Equals("Comment", StringComparison.OrdinalIgnoreCase))
            expectedField = "Comment";
        // Остальные строки игнорируем
    }

    // =========== Разбор строки внутри блока цепи ===========
    private void ParseNetLine(string line, NetInfo net)
    {
        // Первая непустая строка после '(' — имя цепи
        if (string.IsNullOrEmpty(net.NetName))
        {
            net.NetName = line;
            return;
        }

        // Остальные строки — пины; извлекаем обозначение-вывод
        var match = Regex.Match(line, @"^(?<pin>[A-Za-z0-9]+-\d+)");
        if (match.Success)
        {
            net.Pins.Add(match.Groups["pin"].Value);
        }
    }

    // =========== Запись файла соединений ===========
    private void WriteNetFile(List<NetInfo> nets, string folder, string baseName)
    {
        var sb = new StringBuilder();
        foreach (var net in nets)
        {
            sb.AppendLine($"{net.NetName}: {string.Join(", ", net.Pins)}");
        }
        string path = Path.Combine(folder, baseName + "_net.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // =========== Запись файла BOM ===========
    private void WriteBomFile(List<ComponentInfo> components, string folder, string baseName)
    {
        var sb = new StringBuilder();
        foreach (var comp in components)
        {
            string comment = string.IsNullOrEmpty(comp.Comment) ? "" : $" ({comp.Comment})";
            sb.AppendLine($"{comp.Designator}: {comp.PartType}{comment}");
        }
        string path = Path.Combine(folder, baseName + "_bom.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // =========== Запись файла Graphviz DOT ===========
    private void WriteDotFile(List<ComponentInfo> components, List<NetInfo> nets, string folder, string baseName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph Netlist {");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    node [shape=box, style=filled, fillcolor=lightyellow];");
        
        // Узлы компонентов
        foreach (var comp in components)
        {
            string label = $"{comp.Designator}\\n({comp.PartType})";
            sb.AppendLine($"    \"{comp.Designator}\" [label=\"{label}\"];");
        }

        sb.AppendLine();
        sb.AppendLine("    node [shape=ellipse, style=filled, fillcolor=lightblue];");
        
        // Узлы цепей и рёбра
        foreach (var net in nets)
        {
            string netNodeId = $"net_{net.NetName}";
            sb.AppendLine($"    \"{netNodeId}\" [label=\"{net.NetName}\"];");

            // Группируем пины по компонентам
            var componentPins = new Dictionary<string, List<string>>();
            foreach (var pin in net.Pins)
            {
                // pin format: "R1-2"
                int dashIndex = pin.LastIndexOf('-');
                if (dashIndex <= 0) continue; // неверный формат
                string designator = pin.Substring(0, dashIndex);
                string pinNumber = pin.Substring(dashIndex + 1);
                if (!componentPins.ContainsKey(designator))
                    componentPins[designator] = new List<string>();
                componentPins[designator].Add(pinNumber);
            }

            foreach (var kvp in componentPins)
            {
                string compId = kvp.Key;
                string pinList = string.Join(",", kvp.Value);
                string label = kvp.Value.Count > 1 ? $" [label=\"{pinList}\"]" : "";
                sb.AppendLine($"    \"{compId}\" -- \"{netNodeId}\"{label};");
            }
        }

        sb.AppendLine("}");
        string path = Path.Combine(folder, baseName + "_net.dot");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public override void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        base.Dispose();
    }
}
