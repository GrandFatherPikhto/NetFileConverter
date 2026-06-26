using NetFileConverter.Core.Models;

namespace NetFileConverter.Core.Interfaces;

public interface IOutputGenerator
{
    void Generate(NetlistDocument document, string outputDirectory);
}
