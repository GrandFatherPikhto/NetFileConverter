using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NetFileConverter
{
    /// <summary>
    /// Структура для хранения информации об отслеживаемой папке из JSON.
    /// </summary>
    public class WatchedFolder
    {
        public string Path { get; set; } = "";
        public string Format { get; set; } = "KiCad";
    }

    public class Worker : BackgroundService
    {
        private readonly KicadWatchdog _watchdog = new KicadWatchdog();
        private readonly NetlistProcessor _processor = new NetlistProcessor();
        private readonly IConfiguration _configuration;
        
        // Единственное и правильное объявление динамического списка папок
        private readonly List<WatchedFolder> _watchDirectories = new List<WatchedFolder>();

        public Worker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private void LoadDirectoriesFromConfig()
        {
            _watchDirectories.Clear();
            
            // Читаем вложенную секцию "WatcherSettings:SourceDirectories" из вашего JSON
            var foldersSection = _configuration.GetSection("WatcherSettings:SourceDirectories");
            if (!foldersSection.Exists())
            {
                FileLogger.Log("ОШИБКА: Секция 'WatcherSettings:SourceDirectories' не найдена в JSON.");
                return;
            }

            foreach (var child in foldersSection.GetChildren())
            {
                string path = child["Path"] ?? "";
                string format = child["Format"] ?? "KiCad";
                
                if (!string.IsNullOrEmpty(path))
                {
                    _watchDirectories.Add(new WatchedFolder { Path = path, Format = format });
                    FileLogger.Log($"Успешно подгружена папка из JSON: [{format}] {path}");
                }
            }

            FileLogger.Log($"Итого загружено папок из конфигурации: {_watchDirectories.Count}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            FileLogger.Log("Фоновая служба Worker успешно инициализирована .NET-хостом.");

            try
            {
                LoadDirectoriesFromConfig();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Ошибка чтения конфигурации JSON: {ex.Message}");
            }

            // 1. Первоначальный автоматический анализ существующих файлов при старте
            FileLogger.Log("Выполнение первоначального анализа существующих файлов...");
            foreach (var folder in _watchDirectories)
            {
                if (!Directory.Exists(folder.Path))
                {
                    FileLogger.Log($"Папка не найдена при анализе: {folder.Path}");
                    continue;
                }

                // Вызываем наш парсер только для папок с форматом KiCad
                if (folder.Format == "KiCad")
                {
                    foreach (var file in Directory.GetFiles(folder.Path, "*.net"))
                    {
                        _processor.ParseAndSimplify(file);
                    }
                    foreach (var file in Directory.GetFiles(folder.Path, "*_orig.txt"))
                    {
                        _processor.ParseAndSimplify(file);
                    }
                }
                else if (folder.Format == "Protel2")
                {
                    FileLogger.Log($"[Инфо] Папка {folder.Path} имеет формат Protel2. Пропуск анализа KiCad.");
                }
            }

            // 2. Настраиваем KicadWatchdog для мониторинга в реальном времени
            var pathsToWatch = new List<string>();
            foreach (var folder in _watchDirectories)
            {
                if (folder.Format == "KiCad" && Directory.Exists(folder.Path))
                {
                    pathsToWatch.Add(folder.Path);
                }
            }

            if (pathsToWatch.Count > 0)
            {
                _watchdog.StartMonitoring(pathsToWatch);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            _watchdog.Dispose();
            FileLogger.Log("Служба Worker остановлена.");
        }
    }
}
