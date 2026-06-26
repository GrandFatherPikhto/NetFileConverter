using System.Text;
using System.Text.RegularExpressions;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;

namespace NetFileConverter.Infrastructure.Generators;

/// <summary>
/// Генерирует BOM (список компонентов) в текстовом формате (_bom.txt)
/// </summary>
public class BomGenerator : IOutputGenerator
{
    public void Generate(NetlistDocument document, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(document.SourceFileName);
        string outputPath = Path.Combine(outputDirectory, $"{baseName}_bom.txt");

        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine($"=== BOM (Список компонентов) для {document.SourceFileName} ===\n");

        var hasWarnings = document.Metadata?.ContainsKey("HasWarnings") == true;
        if (hasWarnings)
        {
            writer.WriteLine("⚠️⚠️⚠️ ВНИМАНИЕ! НАЙДЕНЫ КОМПОНЕНТЫ БЕЗ ПОСАДОЧНЫХ МЕСТ (FOOTPRINT): ⚠️⚠️⚠️");
            foreach (var comp in document.Components.Where(c => c.Metadata?.ContainsKey("HasWarning") == true))
                writer.WriteLine($"  - {comp.Ref} (Номинал: {comp.Value})");
            writer.WriteLine($"\n{new string('-', 73)}\n");
        }

        writer.WriteLine($"{"Компонент",-12} | {"Номинал",-25} | {"Корпус (Footprint)",-30}");
        writer.WriteLine(new string('-', 73));

        var sortedRefs = document.Components
            .Select(c => c.Ref)
            .OrderBy(r => r, new RefComparer());

        foreach (var refName in sortedRefs)
        {
            var comp = document.Components.First(c => c.Ref == refName);
            writer.WriteLine($"{comp.Ref,-12} | {comp.Value,-25} | {comp.Footprint,-30}");
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