using NetFileConverter.Core.Models;

namespace NetFileConverter.Core.Interfaces;

public interface INetlistSerializer
{
    string Serialize(NetlistDocument document);
    NetlistDocument Deserialize(string json);
    void SerializeToFile(NetlistDocument document, string filePath);
    NetlistDocument DeserializeFromFile(string filePath);
}