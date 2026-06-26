using System.Text.RegularExpressions;
using System.Text;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;

namespace NetFileConverter.Infrastructure.Generators;

/// <summary>
/// Генерирует упрощённый нетлист в текстовом формате (_net.txt)
/// </summary>
public class NetlistGenerator : IOutputGenerator
{
    public void Generate(NetlistDocument document, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(document.SourceFileName);
        string outputPath = Path.Combine(outputDirectory, $"{baseName}_net.txt");

        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine($"=== Упрощённый нетлист для {document.SourceFileName} ===");
        writer.WriteLine("(Изолированные и unconnected цепи отфильтрованы)\n");

        foreach (var net in document.Nets.OrderBy(n => n.Name))
        {
            writer.WriteLine($"Сеть: {net.Name}");
            // Группируем пины по компонентам
            var compGroups = net.Pins
                .GroupBy(p => p.ComponentRef)
                .OrderBy(g => g.Key, new RefComparer());

            foreach (var group in compGroups)
            {
                var pins = group.Select(p => p.Pin).OrderBy(p => p, new PinComparer());
                string pinsStr = string.Join(", ", pins);
                var component = document.Components.FirstOrDefault(c => c.Ref == group.Key);
                string value = component?.Value ?? "?";
                string footprint = component?.Footprint ?? "";
                bool hasWarning = component?.Metadata?.ContainsKey("HasWarning") == true;

                writer.Write($"  └─ {group.Key} ({value})");
                if (!string.IsNullOrEmpty(footprint) && !hasWarning)
                    writer.Write($" [{footprint}]");
                if (hasWarning)
                    writer.Write(" [⚠️ ВНИМАНИЕ: НЕТ КОРПУСА]");
                writer.WriteLine($" -> пины: {pinsStr}");
            }
            writer.WriteLine();
        }
    }

    private class PinComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            bool xIsNum = int.TryParse(x, out int xNum);
            bool yIsNum = int.TryParse(y, out int yNum);

            if (xIsNum && yIsNum) return xNum.CompareTo(yNum);
            if (xIsNum) return -1;
            if (yIsNum) return 1;
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }

    private class RefComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var matchX = Regex.Match(x, @"([A-Za-z]+)(\d+)");
            var matchY = Regex.Match(y, @"([A-Za-z]+)(\d+)");

            if (matchX.Success && matchY.Success)
            {
                string lettersX = matchX.Groups[1].Value;
                string lettersY = matchY.Groups[1].Value;
                int compLetters = string.Compare(lettersX, lettersY, StringComparison.Ordinal);
                if (compLetters != 0) return compLetters;

                int numX = int.Parse(matchX.Groups[2].Value);
                int numY = int.Parse(matchY.Groups[2].Value);
                return numX.CompareTo(numY);
            }
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}