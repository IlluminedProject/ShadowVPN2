using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class PortBindingValidationTests {
    [Theory]
    [InlineData(5000)]
    [InlineData(5010)]
    public void Rejects_udp_listener_on_range_boundary(int port) {
        var configuration = new EntityGlobalConfiguration {
            Protocols = [
                new Hysteria2GlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(5000, 5010) },
                new AwgGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(port) }
            ]
        };
        Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));
    }

    [Fact]
    public void Rejects_mesh_port_inside_range() {
        var configuration = new EntityGlobalConfiguration {
            Protocols = [
                new Hysteria2GlobalSettings {
                    Id = Guid.NewGuid(), ListenPort = new PortBinding(51819, 51821)
                }
            ]
        };
        configuration.AwgSettings.ListenPort = new PortBinding(51820);
        Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));
    }

    [Fact]
    public void Accepts_nonconflicting_range_and_rejects_overlap() {
        var first = new Hysteria2GlobalSettings {
            Id = Guid.NewGuid(), ListenPort = new PortBinding(5000, 5010)
        };
        var second = new Hysteria2GlobalSettings {
            Id = Guid.NewGuid(), ListenPort = new PortBinding(5011, 5020)
        };
        var configuration = new EntityGlobalConfiguration { Protocols = [first, second] };
        GlobalConfigurationValidator.Validate(configuration);
        second.ListenPort = new PortBinding(5010, 5020);
        Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rejects_ranges_for_non_hysteria_protocols(bool naive) {
        ProtocolGlobalSettings protocol;
        if (naive) {
            var settings = new NaiveProxyGlobalSettings();
            settings.GenerateSelfSignedCertificate();
            protocol = settings;
        }
        else {
            protocol = new AwgGlobalSettings();
        }

        protocol.Id = Guid.NewGuid();
        protocol.ListenPort = new PortBinding(5000, 5010);
        var configuration = new EntityGlobalConfiguration { Protocols = [protocol] };

        var exception = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("Only Hysteria2", exception.Message);
    }
}