using System.Text.Json;
using ShadowVPN2.Contracts.Networking;
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
                    PortBinding = new PortBinding(4443, 4450),
                    Enabled = true,
                    ObfsType = "salamander",
                    ObfsPassword = "secret"
                }
            ],
            Transports = [
                new FreeTurnTransportSettingsDto {
                    Id = Guid.NewGuid(),
                    ProtocolId = Guid.NewGuid(),
                    PortBinding = new PortBinding(56000),
                    ObfuscationProfile = FreeTurnObfuscationProfileDto.RtpOpus2,
                    TurnTransport = FreeTurnTurnTransportDto.Udp
                }
            ]
        };

        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<UpdateProtocolsSettingsRequest>(json)!;

        var protocol = Assert.IsType<Hysteria2ProtocolSettingsDto>(Assert.Single(restored.Protocols));
        Assert.Equal(((Hysteria2ProtocolSettingsDto)request.Protocols[0]).PortBinding, protocol.PortBinding);
        using var document = JsonDocument.Parse(json);
        var protocolJson = document.RootElement.GetProperty("Protocols")[0];
        var bindingJson = protocolJson.GetProperty("PortBinding");
        Assert.Equal(4443, bindingJson.GetProperty("ListenPort").GetInt32());
        Assert.Equal(4450, bindingJson.GetProperty("PortRangeEnd").GetInt32());
        Assert.False(bindingJson.TryGetProperty("PortRange", out _));
        Assert.False(protocolJson.TryGetProperty("Port", out _));
        Assert.False(protocolJson.TryGetProperty("PortRange", out _));
        var transport = Assert.IsType<FreeTurnTransportSettingsDto>(Assert.Single(restored.Transports));
        Assert.Equal(request.Transports[0].PortBinding, transport.PortBinding);
        Assert.Equal(FreeTurnObfuscationProfileDto.RtpOpus2, transport.ObfuscationProfile);
        Assert.Equal(FreeTurnTurnTransportDto.Udp, transport.TurnTransport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_port_protocols_expose_numeric_listener_without_binding(bool naive) {
        ProtocolSettingsDto protocol = naive
            ? new NaiveProxyProtocolSettingsDto { ListenPort = 443 }
            : new AwgProtocolSettingsDto { ListenPort = 51821 };
        var json = JsonSerializer.Serialize(protocol);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(naive ? 443 : 51821, root.GetProperty("ListenPort").GetInt32());
        Assert.False(root.TryGetProperty("PortBinding", out _));
        Assert.False(root.TryGetProperty("PortRangeEnd", out _));
        var restored = JsonSerializer.Deserialize<ProtocolSettingsDto>(json);
        if (naive)
            Assert.Equal(443, Assert.IsType<NaiveProxyProtocolSettingsDto>(restored).ListenPort);
        else
            Assert.Equal(51821, Assert.IsType<AwgProtocolSettingsDto>(restored).ListenPort);
    }
}