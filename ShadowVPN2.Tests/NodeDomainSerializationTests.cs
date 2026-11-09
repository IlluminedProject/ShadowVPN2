using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NodeDomainSerializationTests {
    [Theory]
    [InlineData("\" Example.COM. \"", "example.com")]
    [InlineData("null", null)]
    public void Node_domain_should_round_trip_as_nullable_string(string domainJson, string? expected) {
        var json =
            $"{{\"Id\":\"EntityClusterNodes/1\",\"NodeId\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"Test\",\"Domain\":{domainJson}}}";
        var node = JsonSerializer.Deserialize<EntityClusterNode>(json)!;

        node.Domain?.Value.Should().Be(expected);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(node));
        document.RootElement.GetProperty("Domain").GetString().Should().Be(expected);
    }
}