using System;
using System.Collections.Generic;
using System.IO;

namespace NetFileConverter
{
    public class KicadWatchdog : IDisposable
    {
        private readonly NetlistProcessor _processor = new NetlistProcessor();
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Dictionary<string, DateTime> _lastWriteTimes = new Dictionary<string, DateTime>();
        private readonly object _lock = new object();

        public void StartMonitoring(List<string> directories)
        {
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    FileLogger.Log($"ПРЕДУПРЕЖДЕНИЕ: Папка для мониторинга не существует: {dir}");
                    continue;
                }

                // Настраиваем слежку за файлами .net
                var watcher = new FileSystemWatcher(dir, "*.net")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;

                _watchers.Add(watcher);
                FileLogger.Log($"Глаз C# успешно положен на директорию: {dir}");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_lock)
            {
                DateTime now = DateTime.Now;
                // Защита от дребезга (debounce): игнорируем триггеры, если прошло меньше 1.5 секунд
                if (_lastWriteTimes.TryGetValue(e.FullPath, out DateTime lastWrite) && (now - lastWrite).TotalSeconds < 1.5)
                {
                    return;
                }
                _lastWriteTimes[e.FullPath] = now;
            }

            // Даем KiCad 200 миллисекунд, чтобы полностью освободить файл после сохранения
            System.Threading.Thread.Sleep(200);
            _processor.ParseAndSimplify(e.FullPath);
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            FileLogger.Log("Все триггеры FileSystemWatcher успешно остановлены и деаллоцированы.");
        }
    }
}
