using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace NetFileConverter
{
    public class Worker : BackgroundService
    {
        private readonly KicadWatchdog _watchdog = new KicadWatchdog();
        private readonly NetlistProcessor _processor = new NetlistProcessor();

        // Ваши папки проектов
        private readonly List<string> _watchDirectories = new List<string>
        {
            @"D:\Projects\KiCad\Anode\Control_Board_EPM240",
            @"D:\Projects\KiCad\Anode\Power_Board"
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            FileLogger.Log("Фоновая служба Worker успешно инициализирована .NET-хостом.");

            // 1. Первоначальный автоматический анализ существующих файлов при старте
            FileLogger.Log("Выполнение первоначального анализа существующих файлов...");
            foreach (var dir in _watchDirectories)
            {
                if (!Directory.Exists(dir)) continue;

                // Обрабатываем все файлы .net в папках проектов сразу при старте
                foreach (var file in Directory.GetFiles(dir, "*.net"))
                {
                    _processor.ParseAndSimplify(file);
                }
                
                // Также проверяем ваш оригинальный тестовый файл _orig.txt, если он там лежит
                foreach (var file in Directory.GetFiles(dir, "*_orig.txt"))
                {
                    _processor.ParseAndSimplify(file);
                }
            }

            // 2. Передаем папки в KicadWatchdog для отслеживания изменений в реальном времени
            _watchdog.StartMonitoring(_watchDirectories);

            // Поддерживаем жизнь службы, пока .NET-хост не пришлет сигнал остановки
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            // Освобождаем ресурсы при выходе
            _watchdog.Dispose();
            FileLogger.Log("Служба Worker остановлена.");
        }
    }
}
