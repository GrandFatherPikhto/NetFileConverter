using System.Text;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;

namespace NetFileConverter.Infrastructure.Generators;

/// <summary>
/// Генерирует DOT-граф для визуализации в Graphviz (_net.dot)
/// </summary>
public class DotGenerator : IOutputGenerator
{
    public void Generate(NetlistDocument document, string outputDirectory)
    {
        // Проверяем наличие Graphviz (dot.exe) — опционально
        // Если не найден, можно просто логировать или пропустить генерацию.
        // Но пока генерируем всегда.

        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(document.SourceFileName);
        string outputPath = Path.Combine(outputDirectory, $"{baseName}_net.dot");

        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("digraph Netlist {");
        writer.WriteLine("    rankdir=LR;"); // Горизонтальная компоновка
        writer.WriteLine("    node [shape=box, style=rounded];");

        // Узлы: все компоненты
        foreach (var comp in document.Components)
        {
            string label = $"{comp.Ref}\\n{comp.Value}";
            if (!string.IsNullOrEmpty(comp.Footprint) && !comp.Metadata?.ContainsKey("HasWarning") == true)
                label += $"\\n{comp.Footprint}";
            writer.WriteLine($"    \"{comp.Ref}\" [label=\"{label}\"];");
        }

        // Рёбра: для каждой цепи соединяем все компоненты, входящие в неё
        foreach (var net in document.Nets)
        {
            // Получаем список компонентов в этой цепи (уникальные)
            var compRefs = net.Pins.Select(p => p.ComponentRef).Distinct().ToList();
            if (compRefs.Count < 2) continue; // изолированная цепь — не показываем

            // Соединяем все пары (полный граф) или можно сделать последовательную цепь.
            // Для наглядности делаем полный граф между компонентами в цепи.
            for (int i = 0; i < compRefs.Count; i++)
            {
                for (int j = i + 1; j < compRefs.Count; j++)
                {
                    writer.WriteLine($"    \"{compRefs[i]}\" -> \"{compRefs[j]}\" [label=\"{net.Name}\", fontsize=10];");
                }
            }
        }

        writer.WriteLine("}");
    }
}