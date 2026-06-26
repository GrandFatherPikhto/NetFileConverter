using NetFileConverter.Core.Models;
using NetFileConverter.Core.Serialization;
using Xunit;

namespace NetFileConverter.Core.Tests;

public class SerializationTests
{
    [Fact]
    public void SerializeAndDeserialize_ShouldPreserveData()
    {
        // Arrange
        var original = new NetlistDocument
        {
            SourceFileName = "test.net",
            Format = "KiCad",
            ParsedAt = DateTime.UtcNow,
            Components =
            [
                new() { Ref = "R1", Value = "10k", Footprint = "0805" },
                new() { Ref = "C1", Value = "100nF", Footprint = "0805" }
            ],
            Nets =
            [
                new()
                {
                    Name = "GND",
                    Pins =
                    [
                        new() { ComponentRef = "R1", Pin = "2" },
                        new() { ComponentRef = "C1", Pin = "1" }
                    ]
                }
            ]
        };

        var serializer = new JsonNetlistSerializer();

        // Act
        var json = serializer.Serialize(original);
        var deserialized = serializer.Deserialize(json);

        // Assert
        Assert.Equal(original.SourceFileName, deserialized.SourceFileName);
        Assert.Equal(original.Components.Count, deserialized.Components.Count);
        Assert.Equal(original.Nets.Count, deserialized.Nets.Count);
        Assert.Equal(original.Nets[0].Pins.Count, deserialized.Nets[0].Pins.Count);
        Assert.Equal(original.Nets[0].Pins[0].ComponentRef, deserialized.Nets[0].Pins[0].ComponentRef);
    }
}