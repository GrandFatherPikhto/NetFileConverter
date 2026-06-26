using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace NetFileConverter
{
    public class Worker : BackgroundService
    {
        private readonly KicadWatchdog _watchdog = new KicadWatchdog();
        private readonly NetlistProcessor _processor = new NetlistProcessor();
        private readonly IConfiguration _configuration;

        public Worker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            FileLogger.Log("Фоновая служба Worker успешно инициализирована .NET-хостом.");

            // Читаем директории из конфига
            var entries = LoadDirectories();
            FileLogger.Log($"Загружено {entries.Count} директорий из конфига.");

            // Первоначальный анализ существующих файлов
            FileLogger.Log("Выполнение первоначального анализа существующих файлов...");
            foreach (var (dir, format) in entries)
            {
                if (!Directory.Exists(dir))
                {
                    FileLogger.Log($"ПРЕДУПРЕЖДЕНИЕ: Папка не существует: {dir}");
                    continue;
                }

                foreach (var file in Directory.GetFiles(dir, "*.net"))
                    _processor.ParseAndSimplify(file);

                foreach (var file in Directory.GetFiles(dir, "*.NET"))
                    _processor.ParseAndSimplify(file);
            }

            // Запускаем мониторинг
            var dirs = new List<string>();
            foreach (var (dir, _) in entries)
                dirs.Add(dir);
            _watchdog.StartMonitoring(dirs);

            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(1000, stoppingToken);

            _watchdog.Dispose();
            FileLogger.Log("Служба Worker остановлена.");
        }

        private List<(string Dir, string Format)> LoadDirectories()
        {
            var result = new List<(string, string)>();
            var section = _configuration.GetSection("WatcherSettings:SourceDirectories");

            foreach (var child in section.GetChildren())
            {
                string? path   = child["Path"];
                string? format = child["Format"];
                if (!string.IsNullOrWhiteSpace(path))
                    result.Add((path.TrimEnd('\\', '/'), format ?? "KiCad"));
            }

            return result;
        }
    }
}