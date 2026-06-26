using System.Text;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;

namespace NetFileConverter.Infrastructure.Generators;

/// <summary>
/// Генерирует диаграмму в формате Mermaid (для вставки в Markdown)
/// </summary>
public class MermaidGenerator : IOutputGenerator
{
    public void Generate(NetlistDocument document, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(document.SourceFileName);
        string outputPath = Path.Combine(outputDirectory, $"{baseName}_net.mermaid");

        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("```mermaid");
        writer.WriteLine("flowchart LR");

        // Узлы: компоненты
        foreach (var comp in document.Components)
        {
            string label = $"{comp.Ref}\n({comp.Value})";
            if (!string.IsNullOrEmpty(comp.Footprint) && !comp.Metadata?.ContainsKey("HasWarning") == true)
                label += $"\n[{comp.Footprint}]";
            writer.WriteLine($"    {comp.Ref}[\"{label}\"]");
        }

        // Рёбра
        foreach (var net in document.Nets)
        {
            var compRefs = net.Pins.Select(p => p.ComponentRef).Distinct().ToList();
            if (compRefs.Count < 2) continue;

            // Для Mermaid можно использовать соединения без подписей или с ними
            for (int i = 0; i < compRefs.Count; i++)
            {
                for (int j = i + 1; j < compRefs.Count; j++)
                {
                    writer.WriteLine($"    {compRefs[i]} ---|{net.Name}| {compRefs[j]}");
                }
            }
        }

        writer.WriteLine("```");
    }
}