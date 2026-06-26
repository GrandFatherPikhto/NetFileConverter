using System;
using System.IO;
using System.Text;

namespace NetFileConverter
{
    /// <summary>
    /// Простой статический логгер для записи отладочной информации в файл.
    /// </summary>
    public static class FileLogger
    {
        private static string? _logPath;

        /// <summary>
        /// Инициализирует путь к файлу лога.
        /// </summary>
        public static void Initialize(string targetFilePath)
        {
            string? dir = Path.GetDirectoryName(targetFilePath);
            // _logPath = Path.Combine(dir ?? AppContext.BaseDirectory, "kicad_debug_log.txt");
            _logPath = Path.Combine("D:\\Projects\\DotNet\\NetFileConverter\\kicad_debug_log.txt"); // Жёстко задаём путь для отладки

            // При инициализации перезаписываем файл, создавая чистый лог для нового запуска
            try
            {
                File.WriteAllText(_logPath, $"=== СТАРТ ОТЛАДКИ: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось создать файл лога: {ex.Message}");
            }
        }

        /// <summary>
        /// Добавляет строку сообщения в лог-файл.
        /// </summary>
        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(_logPath)) return;

            try
            {
                // Открываем файл в режиме добавления (append)
                using (var writer = new StreamWriter(_logPath, true, Encoding.UTF8))
                {
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                }
            }
            catch
            {
                // Если файл занят или заблокирован, молча игнорируем, чтобы не уронить парсер
            }
        }
    }
}
