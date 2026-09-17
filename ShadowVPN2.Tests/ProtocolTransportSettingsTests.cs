using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ProtocolTransportSettingsTests {
    [Fact]
    public void Global_configuration_should_preserve_transport_settings() {
        var protocol = new WireGuardAmneziaGlobalSettings { Id = Guid.NewGuid() };
        var transport = new FreeTurnTransportSettings {
            Id = Guid.NewGuid(),
            ProtocolId = protocol.Id,
            ListenPort = 56000,
            ObfuscationProfile = null
        };
        var configuration = new EntityGlobalConfiguration {
            Protocols = [protocol],
            Transports = [transport]
        };

        var json = JsonSerializer.Serialize(configuration, DataUtils.DefaultSerializerOptions);
        var restored = JsonSerializer.Deserialize<EntityGlobalConfiguration>(json, DataUtils.DefaultSerializerOptions);

        restored.Should().NotBeNull();
        restored.Protocols.Should().ContainSingle().Which.Should().BeOfType<WireGuardAmneziaGlobalSettings>();
        restored.Transports.Should().ContainSingle().Which.Should().BeOfType<FreeTurnTransportSettings>();
        restored.Transports[0].ProtocolId.Should().Be(protocol.Id);
        restored.Transports[0].ListenPort.Should().Be(56000);
        ((FreeTurnTransportSettings)restored.Transports[0]).ObfuscationProfile.Should().BeNull();
    }

    [Fact]
    public void Protocol_definitions_should_expose_static_socket_kind() {
        Hysteria2GlobalSettings.SocketKind.Should().Be(ProtocolSocketKind.Udp);
        WireGuardAmneziaGlobalSettings.SocketKind.Should().Be(ProtocolSocketKind.Udp);
    }
}