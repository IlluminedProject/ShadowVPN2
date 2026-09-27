using System.Text.Json;
using ShadowVPN2.Contracts.Protocols;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ProtocolContractSerializationTests {
    [Fact]
    public void Protocol_settings_round_trip_with_polymorphic_discriminator() {
        var request = new UpdateProtocolsSettingsRequest {
            Protocols = [
                new Hysteria2ProtocolSettingsDto {
                    Id = Guid.NewGuid(),
                    ListenPort = 4443,
                    Enabled = true,
                    ObfsType = "salamander",
                    ObfsPassword = "secret"
                }
            ],
            Transports = [
                new FreeTurnTransportSettingsDto {
                    Id = Guid.NewGuid(),
                    ProtocolId = Guid.NewGuid(),
                    ListenPort = 56000,
                    ObfuscationProfile = FreeTurnObfuscationProfileDto.RtpOpus2,
                    TurnTransport = FreeTurnTurnTransportDto.Udp
                }
            ]
        };

        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<UpdateProtocolsSettingsRequest>(json)!;

        Assert.IsType<Hysteria2ProtocolSettingsDto>(Assert.Single(restored.Protocols));
        var transport = Assert.IsType<FreeTurnTransportSettingsDto>(Assert.Single(restored.Transports));
        Assert.Equal(FreeTurnObfuscationProfileDto.RtpOpus2, transport.ObfuscationProfile);
        Assert.Equal(FreeTurnTurnTransportDto.Udp, transport.TurnTransport);
    }
}