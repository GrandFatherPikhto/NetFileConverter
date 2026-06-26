using System.Text;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;
using NetFileConverter.Infrastructure.Utils;

namespace NetFileConverter.Infrastructure.Generators;

/// <summary>
/// Генерирует DOT-граф для визуализации в Graphviz (_net.dot)
/// </summary>
public class DotGenerator : IOutputGenerator
{
    private readonly IGraphvizRenderer _renderer;

    public DotGenerator(IGraphvizRenderer renderer)
    {
        _renderer = renderer; // !!! Раскомментировано
    }

    public void Generate(NetlistDocument document, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(document.SourceFileName);
        string dotFilePath = Path.Combine(outputDirectory, $"{baseName}_net.dot"); // переименовано в dotFilePath

        using var writer = new StreamWriter(dotFilePath, false, Encoding.UTF8);
        writer.WriteLine("digraph Netlist {");
        writer.WriteLine("    rankdir=LR;");
        writer.WriteLine("    node [shape=box, style=rounded];");

        foreach (var comp in document.Components)
        {
            string label = $"{comp.Ref}\\n{comp.Value}";
            if (!string.IsNullOrEmpty(comp.Footprint) && !comp.Metadata?.ContainsKey("HasWarning") == true)
                label += $"\\n{comp.Footprint}";
            writer.WriteLine($"    \"{comp.Ref}\" [label=\"{label}\"];");
        }

        foreach (var net in document.Nets)
        {
            var compRefs = net.Pins.Select(p => p.ComponentRef).Distinct().ToList();
            if (compRefs.Count < 2) continue;

            for (int i = 0; i < compRefs.Count; i++)
            {
                for (int j = i + 1; j < compRefs.Count; j++)
                {
                    writer.WriteLine($"    \"{compRefs[i]}\" -> \"{compRefs[j]}\" [label=\"{net.Name}\", fontsize=10];");
                }
            }
        }

        writer.WriteLine("}");

        // Попытка сгенерировать PNG из DOT-файла
        var pngPath = Path.ChangeExtension(dotFilePath, ".png");
        _renderer.TryRenderDotToPng(dotFilePath, pngPath);
    }
}