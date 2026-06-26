using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;
using NetFileConverter.Infrastructure.Parsers;
using NetFileConverter.Infrastructure.Generators;

namespace NetFileConverter;

public class Worker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly INetlistParser _parser;
    private readonly IEnumerable<IOutputGenerator> _generators;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _pendingFiles = new();
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private CancellationToken _stoppingToken;

    public Worker(IConfiguration configuration, INetlistParser parser, IEnumerable<IOutputGenerator> generators)
    {
        _configuration = configuration;
        _parser = parser;
        _generators = generators;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        FileLogger.Log("Worker инициализирован с новой архитектурой.");

        var entries = LoadDirectories();
        FileLogger.Log($"Загружено {entries.Count} директорий из конфига.");

        // Первоначальная обработка существующих файлов
        await ProcessExistingFiles(entries);

        // Запуск мониторинга
        StartMonitoring(entries);

        // Фоновая задача для обработки очереди файлов
        _ = Task.Run(() => ProcessQueueAsync(stoppingToken), stoppingToken);

        // Ожидаем сигнал остановки
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private List<(string Path, string Format)> LoadDirectories()
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

    private async Task ProcessExistingFiles(List<(string Path, string Format)> entries)
    {
        foreach (var (dir, format) in entries)
        {
            if (!Directory.Exists(dir))
            {
                FileLogger.Log($"ПРЕДУПРЕЖДЕНИЕ: Папка не существует: {dir}");
                continue;
            }

            var files = Directory.GetFiles(dir, "*.net")
                .Concat(Directory.GetFiles(dir, "*.NET"))
                .Distinct();

            foreach (var file in files)
            {
                await ProcessFile(file);
            }
        }
    }

    private void StartMonitoring(List<(string Path, string Format)> entries)
    {
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

            FileLogger.Log($"Мониторинг запущен для: {dir} (формат: {format})");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Дедупликация: добавляем файл в очередь с задержкой
        _pendingFiles.AddOrUpdate(e.FullPath, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_debounceDelay, cancellationToken);

            var now = DateTime.UtcNow;
            var readyFiles = _pendingFiles
                .Where(kvp => (now - kvp.Value) > _debounceDelay)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var filePath in readyFiles)
            {
                if (_pendingFiles.TryRemove(filePath, out _))
                {
                    try
                    {
                        await ProcessFile(filePath);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Log($"Ошибка обработки файла {filePath}: {ex.Message}");
                    }
                }
            }
        }
    }

    private async Task ProcessFile(string filePath)
    {
        // Ожидаем освобождения файла (если он ещё пишется)
        await WaitForFileReady(filePath);

        await _processingSemaphore.WaitAsync(_stoppingToken);
        try
        {
            FileLogger.Log($"Обработка: {Path.GetFileName(filePath)}");

            // Парсим
            var document = _parser.Parse(filePath);

            // Сохраняем JSON-версию (опционально)
            string outDir = Path.Combine(Path.GetDirectoryName(filePath)!, "out");
            Directory.CreateDirectory(outDir);
            string jsonPath = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(filePath)}.json");
            var serializer = new JsonNetlistSerializer();
            serializer.SerializeToFile(document, jsonPath);

            // Генерируем выходные файлы
            foreach (var generator in _generators)
            {
                generator.Generate(document, outDir);
            }

            FileLogger.Log($"Успешно обработан: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            FileLogger.Log($"Ошибка обработки {filePath}: {ex.Message}");
            throw;
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private async Task WaitForFileReady(string filePath)
    {
        const int maxAttempts = 10;
        const int delayMs = 200;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return;
                }
            }
            catch (IOException)
            {
                await Task.Delay(delayMs, _stoppingToken);
            }
        }
        throw new TimeoutException($"Файл {filePath} не стал доступен после {maxAttempts * delayMs} мс.");
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _processingSemaphore.Dispose();
        base.Dispose();
    }
}