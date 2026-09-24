using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ProtocolTransportSettingsTests {
    [Fact]
    public void Global_configuration_should_preserve_transport_settings() {
        var protocol = new AwgGlobalSettings { Id = Guid.NewGuid(), H1 = "100-200", Jc = 5 };
        var transport = new FreeTurnTransportSettings {
            Id = Guid.NewGuid(),
            ProtocolId = protocol.Id,
            ListenPort = 56000,
            ObfuscationProfile = null
        };
        var configuration = new EntityGlobalConfiguration {
            AwgSettings = AwgGlobalSettings.CreateForCluster(),
            Protocols = [protocol],
            Transports = [transport]
        };
        configuration.AwgSettings.H1 = "300-400";

        var json = JsonSerializer.Serialize(configuration, DataUtils.DefaultSerializerOptions);
        var restored = JsonSerializer.Deserialize<EntityGlobalConfiguration>(json, DataUtils.DefaultSerializerOptions);

        restored.Should().NotBeNull();
        var restoredProtocol = restored.Protocols.Should().ContainSingle().Which.Should().BeOfType<AwgGlobalSettings>()
            .Subject;
        restoredProtocol.H1.Should().Be("100-200");
        restoredProtocol.Jc.Should().Be(5);
        restored.AwgSettings.H1.Should().Be("300-400");
        restored.Transports.Should().ContainSingle().Which.Should().BeOfType<FreeTurnTransportSettings>();
        restored.Transports[0].ProtocolId.Should().Be(protocol.Id);
        restored.Transports[0].ListenPort.Should().Be(56000);
        ((FreeTurnTransportSettings)restored.Transports[0]).ObfuscationProfile.Should().BeNull();
    }

    [Fact]
    public void Protocol_definitions_should_expose_static_socket_kind() {
        Hysteria2GlobalSettings.SocketKind.Should().Be(ProtocolSocketKind.Udp);
        AwgGlobalSettings.SocketKind.Should().Be(ProtocolSocketKind.Udp);
    }
}