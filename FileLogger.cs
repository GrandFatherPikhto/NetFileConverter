using System;
using System.IO;
using System.Text;

namespace NetFileConverter
{
    /// <summary>
    /// Профессиональный потокобезопасный логгер с автоопределением путей в %APPDATA%.
    /// </summary>
    public static class FileLogger
    {
        private static readonly string _logPath;
        private static readonly object _lock = new object();

        static FileLogger()
        {
            // Автоматически находим путь к папке AppData/Roaming
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = Path.Combine(appData, "NetFileConverter");
            
            // Гарантируем, что папка существует
            Directory.CreateDirectory(logDir);
            
            _logPath = Path.Combine(logDir, "kicad_debug_log.txt");

            try
            {
                // Перезаписываем лог при старте приложения
                File.WriteAllText(_logPath, $"=== СТАРТ СИСТЕМНОГО ЛОГА: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось инициализировать лог-файл: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    using (var writer = new StreamWriter(_logPath, true, Encoding.UTF8))
                    {
                        writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка записи в лог: {ex.Message}");
                }
            }
        }
    }
}