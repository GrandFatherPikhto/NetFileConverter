using NetFileConverter.Core.Models;

namespace NetFileConverter.Core.Interfaces;

public interface INetlistParser
{
    NetlistDocument Parse(string filePath);
}