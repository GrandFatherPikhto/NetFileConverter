using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;
using NetFileConverter.Core.Serialization;

namespace NetFileConverter;

public class Worker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<Worker> _logger;
    private readonly IEnumerable<INetlistParser> _parsers;
    private readonly IEnumerable<IOutputGenerator> _generators;
    private readonly INetlistSerializer _serializer;
    private readonly ConcurrentDictionary<string, DateTime> _lastProcessed = new();
    private readonly ConcurrentQueue<string> _queue = new();
    // private readonly Timer _timer;
    private readonly System.Threading.Timer _timer;
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _isProcessing;

    public Worker(
        IConfiguration configuration,
        ILogger<Worker> logger,
        IEnumerable<INetlistParser> parsers,
        IEnumerable<IOutputGenerator> generators,
        INetlistSerializer serializer)
    {
        _configuration = configuration;
        _logger = logger;
        _parsers = parsers;
        _generators = generators;
        _serializer = serializer;
        // _timer = new Timer(ProcessQueue, null, Timeout.Infinite, Timeout.Infinite);
        _timer = new System.Threading.Timer(ProcessQueue, null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker запущен");

        // Загружаем настройки
        var entries = LoadDirectories();
        _logger.LogInformation("Загружено {Count} директорий", entries.Count);

        // Первоначальная обработка существующих файлов
        foreach (var (dir, format) in entries)
        {
            if (!Directory.Exists(dir))
            {
                _logger.LogWarning("Папка не существует: {Dir}", dir);
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.net", SearchOption.TopDirectoryOnly))
            {
                _queue.Enqueue(file);
            }
        }
        ProcessQueue(null);

        // Запускаем мониторинг
        foreach (var (dir, format) in entries)
        {
            if (!Directory.Exists(dir)) continue;

            var watcher = new FileSystemWatcher(dir, "*.net")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            _watchers.Add(watcher);
            _logger.LogInformation("Начат мониторинг папки: {Dir} (формат: {Format})", dir, format);
        }

        // Ждём остановки
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        // Остановка
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _timer.Dispose();

        _logger.LogInformation("Worker остановлен");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Дедупликация: не обрабатываем один файл чаще чем раз в 1.5 секунды
        var now = DateTime.Now;
        if (_lastProcessed.TryGetValue(e.FullPath, out var last) && (now - last).TotalSeconds < 1.5)
            return;

        _lastProcessed[e.FullPath] = now;
        _queue.Enqueue(e.FullPath);
        _timer.Change(200, Timeout.Infinite); // Задержка 200 мс перед обработкой
    }

    private void ProcessQueue(object? state)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            while (_queue.TryDequeue(out var filePath))
            {
                try
                {
                    ProcessFile(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки файла {File}", filePath);
                }
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void ProcessFile(string filePath)
    {
        _logger.LogInformation("Обработка файла: {File}", Path.GetFileName(filePath));

        // Определяем формат
        var format = DetectFormat(filePath);
        if (format == null)
        {
            _logger.LogWarning("Неизвестный формат файла: {File}", filePath);
            return;
        }

        // Выбираем парсер
        var parser = _parsers.FirstOrDefault(p =>
            (format == "KiCad" && p is Infrastructure.Parsers.KiCadParser) ||
            (format == "Protel2" && p is Infrastructure.Parsers.Protel2Parser));

        if (parser == null)
        {
            _logger.LogWarning("Парсер для формата {Format} не найден", format);
            return;
        }

        // Парсим
        var document = parser.Parse(filePath);
        _logger.LogInformation("Парсинг завершён: {Components} компонентов, {Nets} цепей",
            document.Components.Count, document.Nets.Count);

        // Сохраняем JSON (опционально, для отладки)
        var outDir = Path.Combine(Path.GetDirectoryName(filePath)!, "out");
        var jsonPath = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(filePath)}.json");
        _serializer.SerializeToFile(document, jsonPath);

        // Генерируем выходные файлы
        foreach (var generator in _generators)
        {
            generator.Generate(document, outDir);
        }

        _logger.LogInformation("Файл {File} обработан успешно", Path.GetFileName(filePath));
    }

    private string? DetectFormat(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buf = new byte[64];
            int n = fs.Read(buf, 0, buf.Length);
            string header = Encoding.ASCII.GetString(buf, 0, n);

            if (header.StartsWith("PROTEL NETLIST", StringComparison.OrdinalIgnoreCase))
                return "Protel2";
            if (header.TrimStart().StartsWith("(export", StringComparison.OrdinalIgnoreCase))
                return "KiCad";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка определения формата файла {File}", filePath);
        }

        return null;
    }

    private List<(string Dir, string Format)> LoadDirectories()
    {
        var result = new List<(string, string)>();
        var section = _configuration.GetSection("WatcherSettings:SourceDirectories");

        foreach (var child in section.GetChildren())
        {
            string? path = child["Path"];
            string? format = child["Format"];
            if (!string.IsNullOrWhiteSpace(path))
                result.Add((path.TrimEnd('\\', '/'), format ?? "KiCad"));
        }

        return result;
    }
}