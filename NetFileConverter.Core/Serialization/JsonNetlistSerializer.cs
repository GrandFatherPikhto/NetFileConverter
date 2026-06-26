using System.Text.Json;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Models;

namespace NetFileConverter.Core.Serialization;

public class JsonNetlistSerializer : INetlistSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            // Если нужно сериализовать DateTime в ISO, он и так сериализуется по умолчанию.
        }
    };

    public string Serialize(NetlistDocument document)
    {
        return JsonSerializer.Serialize(document, Options);
    }

    public NetlistDocument Deserialize(string json)
    {
        return JsonSerializer.Deserialize<NetlistDocument>(json, Options)
               ?? throw new InvalidOperationException("Deserialization returned null.");
    }

    public void SerializeToFile(NetlistDocument document, string filePath)
    {
        var json = Serialize(document);
        File.WriteAllText(filePath, json);
    }

    public NetlistDocument DeserializeFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return Deserialize(json);
    }
}