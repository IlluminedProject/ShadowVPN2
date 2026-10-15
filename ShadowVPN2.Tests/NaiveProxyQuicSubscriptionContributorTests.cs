using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NaiveProxyQuicSubscriptionContributorTests {
    [Fact]
    public async Task Quic_subscription_reuses_naive_credentials_and_fallback_certificate_pin() {
        using var store = new DocumentStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var globalConfiguration = new GlobalConfigurationService(services,
            NullLogger<GlobalConfigurationService>.Instance);
        using var certificates = new ManagedCertificateService(store, NullLogger<ManagedCertificateService>.Instance);
        using var clients = new ClientService(store, NullLogger<ClientService>.Instance);
        var contributor = new NaiveProxyQuicSubscriptionContributor(certificates, globalConfiguration, clients);
        var settings = new NaiveProxyQuicGlobalSettings {
            ListenPort = new PortBinding(8443),
            MainDomain = HostName.Parse("vpn.example.com")
        };
        settings.GenerateSelfSignedCertificate();
        var credentials = new NaiveProxyClientSettings { Username = "existing-user", Password = "existing-password" };
        var client = new EntityClient { Name = "device", NaiveProxy = credentials };

        var connection = await contributor.CreateAsync(client, settings, HostName.Parse("203.0.113.10"),
            HostName.Parse("node.example.com"));

        var naive = connection.Should().BeOfType<NaiveProxyConnectionInfo>().Subject;
        contributor.Protocol.Should().Be(settings.Protocol);
        naive.Quic.Should().BeTrue();
        naive.ServerAddress.Should().Be(HostName.Parse("203.0.113.10"));
        naive.ServerPort.Should().Be(8443);
        naive.Username.Should().Be(credentials.Username);
        naive.Password.Should().Be(credentials.Password);
        client.NaiveProxy.Should().BeSameAs(credentials);
        naive.Sni.Should().Be(HostName.Parse("shadowvpn.local"));
        naive.Insecure.Should().BeTrue();
        naive.PinSha256.Should().Be(settings.GetCertificateFingerprint());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Internal_naive_connection_round_trips_quic_flag(bool quic) {
        ProtocolConnectionInfo connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"),
            ServerPort = 8443,
            Quic = quic,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("vpn.example.com")
        };

        var json = JsonSerializer.Serialize(connection);
        var restored = JsonSerializer.Deserialize<ProtocolConnectionInfo>(json);

        restored.Should().BeOfType<NaiveProxyConnectionInfo>().Which.Quic.Should().Be(quic);
    }
}