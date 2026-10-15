using System.Security.Cryptography.X509Certificates;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NaiveProxyQuicTests {
    [Fact]
    public void Certificate_regeneration_replaces_fallback_certificate() {
        var settings = new NaiveProxyQuicGlobalSettings();
        IProtocolSettingsCertificateRegeneratable generator = settings;
        generator.GenerateSelfSignedCertificate();
        var original = settings.GetCertificateFingerprint();
        Assert.NotNull(original);

        generator.GenerateSelfSignedCertificate();

        Assert.NotNull(settings.GetCertificateFingerprint());
        Assert.NotEqual(original, settings.GetCertificateFingerprint());
        using var certificate = X509Certificate2.CreateFromPem(settings.TlsCertificatePem, settings.TlsKeyPem);
        Assert.True(certificate.HasPrivateKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid certificate")]
    public void Unavailable_certificate_has_no_fingerprint(string certificatePem) {
        var settings = new NaiveProxyQuicGlobalSettings { TlsCertificatePem = certificatePem };

        Assert.Null(settings.GetCertificateFingerprint());
    }

    [Fact]
    public void Unconfigured_port_is_rejected() {
        var settings = new NaiveProxyQuicGlobalSettings {
            Id = Guid.NewGuid(), TlsCertificatePem = "certificate", TlsKeyPem = "private-key"
        };
        var configuration = new EntityGlobalConfiguration { Protocols = [settings] };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("listen port must be between 1 and 65535", error.Message);
    }

    [Theory]
    [InlineData("", "private-key")]
    [InlineData("certificate", "")]
    [InlineData(" ", "private-key")]
    [InlineData("certificate", " ")]
    public void Requires_both_tls_certificate_and_key(string certificate, string key) {
        var settings = CreateQuic(8443);
        settings.TlsCertificatePem = certificate;
        settings.TlsKeyPem = key;
        var configuration = new EntityGlobalConfiguration { Protocols = [settings] };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("requires a TLS certificate and private key", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rejects_other_udp_protocol_on_same_port(int protocolKind) {
        ProtocolGlobalSettings other = protocolKind switch {
            0 => new Hysteria2GlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(8443) },
            1 => new AwgGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(8443) },
            _ => CreateQuic(8443)
        };
        var configuration = new EntityGlobalConfiguration { Protocols = [CreateQuic(8443), other] };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("used more than once", error.Message);
    }

    [Fact]
    public void Allows_tcp_naive_on_same_number_with_separate_udp_binding() {
        var tcp = CreateTcp(8443);
        var quic = CreateQuic(8443);
        var configuration = new EntityGlobalConfiguration { Protocols = [tcp, quic] };

        GlobalConfigurationValidator.Validate(configuration);
    }

    [Theory]
    [InlineData(5000, false)]
    [InlineData(5005, false)]
    [InlineData(5010, false)]
    [InlineData(5000, true)]
    [InlineData(5005, true)]
    [InlineData(5010, true)]
    public void Rejects_udp_range_overlap_regardless_of_protocol_order(int port, bool quicFirst) {
        var quic = CreateQuic(port);
        var ranged = new Hysteria2GlobalSettings {
            Id = Guid.NewGuid(), ListenPort = new PortBinding(5000, 5010)
        };
        var configuration = new EntityGlobalConfiguration {
            Protocols = quicFirst ? [quic, ranged] : [ranged, quic]
        };

        Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));
    }

    [Theory]
    [InlineData(4999)]
    [InlineData(5011)]
    public void Allows_udp_port_adjacent_to_range(int port) {
        var configuration = new EntityGlobalConfiguration {
            Protocols = [
                new Hysteria2GlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(5000, 5010) },
                CreateQuic(port)
            ]
        };

        GlobalConfigurationValidator.Validate(configuration);
    }

    [Fact]
    public void Rejects_ranges_on_quic_listener() {
        var quic = CreateQuic(5000);
        quic.ListenPort = new PortBinding(5000, 5010);
        var configuration = new EntityGlobalConfiguration { Protocols = [quic] };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("Only Hysteria2", error.Message);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(5005)]
    [InlineData(5010)]
    public void Rejects_quic_listener_overlapping_udp_freeturn_range(int port) {
        var owner = new AwgGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(6000) };
        var configuration = new EntityGlobalConfiguration {
            Protocols = [owner, CreateQuic(port)],
            Transports = [CreateFreeTurn(owner, new PortBinding(5000, 5010))]
        };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("conflicts with another port binding", error.Message);
    }

    [Fact]
    public void Allows_quic_listener_overlapping_tcp_freeturn_range() {
        var owner = CreateTcp(6000);
        var configuration = new EntityGlobalConfiguration {
            Protocols = [owner, CreateQuic(5005)],
            Transports = [CreateFreeTurn(owner, new PortBinding(5000, 5010))]
        };

        GlobalConfigurationValidator.Validate(configuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quic_freeturn_uses_udp_collision_semantics(bool collideWithListener) {
        var quic = CreateQuic(8443);
        var other = new AwgGlobalSettings { Id = Guid.NewGuid(), ListenPort = new PortBinding(6000) };
        var configuration = new EntityGlobalConfiguration {
            Protocols = [quic, other],
            Transports = [CreateFreeTurn(quic, new PortBinding(5000, 5010))]
        };
        if (collideWithListener)
            other.ListenPort = new PortBinding(5005);
        else
            configuration.Transports.Add(CreateFreeTurn(other, new PortBinding(5010, 5020)));

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("conflicts with another port binding", error.Message);
    }

    [Fact]
    public void Quic_freeturn_can_share_tcp_listener_number() {
        var quic = CreateQuic(8443);
        var configuration = new EntityGlobalConfiguration {
            Protocols = [quic, CreateTcp(5005)],
            Transports = [CreateFreeTurn(quic, new PortBinding(5000, 5010))]
        };

        GlobalConfigurationValidator.Validate(configuration);
    }

    [Fact]
    public void Rejects_quic_listener_on_mesh_port_even_when_disabled() {
        var configuration = new EntityGlobalConfiguration { Protocols = [CreateQuic(51820)] };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("reserved for cluster mesh", error.Message);
    }

    [Fact]
    public void Moving_mesh_to_quic_listener_is_rejected() {
        var configuration = new EntityGlobalConfiguration { Protocols = [CreateQuic(8443)] };
        GlobalConfigurationValidator.ValidateMeshPort(configuration);
        configuration.AwgSettings.ListenPort = new PortBinding(8443);

        var error = Assert.Throws<ArgumentException>(() =>
            GlobalConfigurationValidator.ValidateMeshPort(configuration));

        Assert.Contains("reserved for cluster mesh", error.Message);
    }

    [Theory]
    [InlineData(51819)]
    [InlineData(51820)]
    [InlineData(51821)]
    public void Rejects_quic_freeturn_range_containing_mesh(int meshPort) {
        var quic = CreateQuic(8443);
        var configuration = new EntityGlobalConfiguration {
            AwgSettings = new AwgGlobalSettings { ListenPort = new PortBinding(meshPort) },
            Protocols = [quic],
            Transports = [CreateFreeTurn(quic, new PortBinding(51819, 51821))]
        };

        var error = Assert.Throws<ArgumentException>(() => GlobalConfigurationValidator.Validate(configuration));

        Assert.Contains("reserved for cluster mesh", error.Message);
    }

    private static NaiveProxyQuicGlobalSettings CreateQuic(int port) {
        return new NaiveProxyQuicGlobalSettings {
            Id = Guid.NewGuid(), ListenPort = new PortBinding(port),
            TlsCertificatePem = "certificate", TlsKeyPem = "private-key"
        };
    }

    private static NaiveProxyGlobalSettings CreateTcp(int port) {
        return new NaiveProxyGlobalSettings {
            Id = Guid.NewGuid(), ListenPort = new PortBinding(port),
            TlsCertificatePem = "certificate", TlsKeyPem = "private-key"
        };
    }

    private static FreeTurnTransportSettings CreateFreeTurn(ProtocolGlobalSettings owner, PortBinding binding) {
        return new FreeTurnTransportSettings {
            Id = Guid.NewGuid(), ProtocolId = owner.Id, ListenPort = binding
        };
    }
}