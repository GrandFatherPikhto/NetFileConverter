using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NetFileConverter
{
    public class Worker : BackgroundService
    {
        private readonly KicadWatchdog _watchdog = new KicadWatchdog();
        private readonly NetlistProcessor _processor = new NetlistProcessor();
        private readonly IConfiguration _configuration;
        private readonly List<string> _watchDirectories = new List<string>();

        // Конструктор теперь принимает конфигурацию через Dependency Injection автоматически
        public Worker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Загружает список папок из секции конфигурации appsettings.json
        /// </summary>
        // Вверху класса вместо List<string> _watchDirectories делаем список объектов:
        private readonly List<WatchedFolder> _watchDirectories = new List<WatchedFolder>();

        private void LoadDirectoriesFromConfig()
        {
            _watchDirectories.Clear();
            
            // Читаем правильную секцию "WatcherSettings:SourceDirectories"
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

            // Динамически загружаем папки из JSON
            try
            {
                LoadDirectoriesFromConfig();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Ошибка чтения конфигурации JSON: {ex.Message}");
            }

            if (_watchDirectories.Count == 0)
            {
                FileLogger.Log("ВНИМАНИЕ: Список отслеживаемых папок пуст. Ожидание настройки через форму.");
            }

            // 1. Первоначальный автоматический анализ существующих файлов при старте
            FileLogger.Log("Выполнение первоначального анализа существующих файлов...");
            foreach (var dir in _watchDirectories)
            {
                if (!Directory.Exists(dir))
                {
                    FileLogger.Log($"Папка не найдена при анализе: {dir}");
                    continue;
                }

                // Обрабатываем штатные файлы .net
                foreach (var file in Directory.GetFiles(dir, "*.net"))
                {
                    _processor.ParseAndSimplify(file);
                }
                
                // Проверяем тестовые файлы конфигурации
                foreach (var file in Directory.GetFiles(dir, "*_orig.txt"))
                {
                    _processor.ParseAndSimplify(file);
                }
            }

            // 2. Передаем динамические папки в KicadWatchdog
            if (_watchDirectories.Count > 0)
            {
                _watchdog.StartMonitoring(_watchDirectories);
            }

            // Поддерживаем жизнь службы, пока .NET-хост работает
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            // Освобождаем ресурсы при выходе
            _watchdog.Dispose();
            FileLogger.Log("Служба Worker остановлена.");
        }
    }

    private class WatchedFolder
    {
        public string Path { get; set; } = "";
        public string Format { get; set; } = "KiCad";
    }
}
