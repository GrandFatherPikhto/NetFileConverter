using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace NetFileConverter.Infrastructure.Utils;

public interface IGraphvizRenderer
{
    bool TryRenderDotToPng(string dotContent, string outputPngPath);
}

public class GraphvizRenderer : IGraphvizRenderer
{
    private readonly ILogger<GraphvizRenderer> _logger;
    private readonly bool _enabled;
    private readonly string? _dotPath;

    public GraphvizRenderer(ILogger<GraphvizRenderer> logger, IConfiguration configuration)
    {
        _logger = logger;
        var section = configuration.GetSection("Graphviz");
        _enabled = section.GetValue<bool>("RenderPng", true);
        _dotPath = section.GetValue<string>("DotPath");

        if (_enabled && string.IsNullOrEmpty(_dotPath))
        {
            _dotPath = FindDotExecutable();
        }

        if (_enabled && string.IsNullOrEmpty(_dotPath))
        {
            _logger.LogWarning("Graphviz не найден. Генерация PNG будет пропущена.");
            _enabled = false;
        }
        else if (_enabled)
        {
            _logger.LogInformation("Graphviz найден: {DotPath}", _dotPath);
        }
    }

    public bool TryRenderDotToPng(string dotContent, string outputPngPath)
    {
        if (!_enabled || string.IsNullOrEmpty(_dotPath))
        {
            _logger.LogDebug("Graphviz отключён или не найден, пропуск рендеринга PNG");
            return false;
        }

        _logger.LogInformation("DOT content preview: {Preview}", dotContent.Substring(0, Math.Min(100, dotContent.Length)));        

        try
        {
            // Логируем начало содержимого для диагностики
            var preview = dotContent.Length > 100 ? dotContent.Substring(0, 100) : dotContent;
            _logger.LogInformation("DOT content preview: {Preview}", preview);

            var startInfo = new ProcessStartInfo
            {
                FileName = _dotPath,
                Arguments = $"-Tpng -o \"{outputPngPath}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Не удалось запустить процесс dot.exe");
                return false;
            }

            // Передаём содержимое через STDIN как UTF-8 без BOM
            using (var stdin = process.StandardInput)
            {
                // Явно используем UTF-8 без BOM
                var bytes = Encoding.UTF8.GetBytes(dotContent);
                stdin.BaseStream.Write(bytes, 0, bytes.Length);
                stdin.BaseStream.Flush();
                stdin.Close();
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                _logger.LogError("Ошибка рендеринга PNG: {Error}", error);
                return false;
            }

            _logger.LogInformation("PNG создан: {PngPath}", outputPngPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Исключение при рендеринге PNG");
            return false;
        }
    }

    private static string? FindDotExecutable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            var fullPath = Path.Combine(p, "dot.exe");
            if (File.Exists(fullPath))
                return fullPath;
        }

        var commonPaths = new[]
        {
            @"C:\Program Files\Graphviz\bin\dot.exe",
            @"C:\Program Files (x86)\Graphviz\bin\dot.exe",
            @"C:\Graphviz\bin\dot.exe"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}