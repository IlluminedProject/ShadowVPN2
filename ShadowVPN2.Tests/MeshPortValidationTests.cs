using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class MeshPortValidationTests {
    [Theory]
    [InlineData(51820, false)]
    [InlineData(56000, true)]
    public void Udp_protocol_should_not_use_mesh_port(int port, bool amnezia) {
        var configuration = new EntityGlobalConfiguration {
            AwgSettings = new AwgGlobalSettings { ListenPort = new PortBinding(port) },
            Protocols = [
                new AwgGlobalSettings
                    { Id = Guid.NewGuid(), ListenPort = new PortBinding(port), Jc = amnezia ? 6u : 0u }
            ]
        };

        var action = () => GlobalConfigurationValidator.Validate(configuration);

        action.Should().Throw<ArgumentException>().Which.Message.Should().Contain("reserved for cluster mesh");
    }

    [Fact]
    public void FreeTurn_should_not_use_mesh_port() {
        var protocol = new AwgGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(51821) };
        var configuration = new EntityGlobalConfiguration {
            AwgSettings = new AwgGlobalSettings { ListenPort = new PortBinding(56000) },
            Protocols = [protocol],
            Transports = [
                new FreeTurnTransportSettings
                    { Id = Guid.NewGuid(), ProtocolId = protocol.Id, ListenPort = new PortBinding(56000) }
            ]
        };

        var action = () => GlobalConfigurationValidator.Validate(configuration);

        action.Should().Throw<ArgumentException>().Which.Message.Should().Contain("reserved for cluster mesh");
    }

    [Fact]
    public void Tcp_protocol_can_share_mesh_port_number() {
        var protocol = new NaiveProxyGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(51820) };
        protocol.GenerateSelfSignedCertificate();
        var configuration = new EntityGlobalConfiguration { Protocols = [protocol] };

        GlobalConfigurationValidator.Validate(configuration);
        configuration.Protocols.Single().ListenPort.ListenPort.Should()
            .Be(configuration.AwgSettings.ListenPort.ListenPort);
    }

    [Fact]
    public void Moving_mesh_port_to_existing_udp_protocol_should_be_rejected() {
        var configuration = new EntityGlobalConfiguration {
            Protocols = [new Hysteria2GlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(4443) }]
        };
        GlobalConfigurationValidator.ValidateMeshPort(configuration);
        configuration.AwgSettings.ListenPort = new PortBinding(4443);

        var action = () => GlobalConfigurationValidator.ValidateMeshPort(configuration);

        action.Should().Throw<ArgumentException>().Which.Message.Should().Contain("reserved for cluster mesh");
    }
}