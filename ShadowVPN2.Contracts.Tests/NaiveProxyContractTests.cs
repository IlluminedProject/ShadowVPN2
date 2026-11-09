using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Protocols;
using Xunit;

namespace ShadowVPN2.Contracts.Tests;

public sealed class NaiveProxyContractTests {
    [Fact]
    public void Naive_protocol_dto_round_trips_with_polymorphic_discriminator() {
        var request = new UpdateProtocolsSettingsRequest {
            Protocols = [new NaiveProxyProtocolSettingsDto { Id = Guid.NewGuid(), ListenPort = 443, Enabled = true }]
        };

        var restored = JsonSerializer.Deserialize<UpdateProtocolsSettingsRequest>(JsonSerializer.Serialize(request));

        restored.Should().NotBeNull();
        restored!.Protocols.Should().ContainSingle().Which.Should().BeOfType<NaiveProxyProtocolSettingsDto>();
    }
}