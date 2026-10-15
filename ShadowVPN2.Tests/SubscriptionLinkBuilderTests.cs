using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Frontend.Api;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionLinkBuilderTests {
    [Theory]
    [InlineData("vpn.example.com", "vpn.example.com")]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    public void CreateHysteria2ShareUrl_escapes_credentials_and_keeps_connection_options(string host, string uriHost) {
        var connection = new Hysteria2ConnectionInfo {
            ServerAddress = HostName.Parse(host),
            PortBinding = new PortBinding(443),
            Password = "password with spaces",
            ObfsType = "salamander",
            ObfsPassword = "obfs&key",
            Sni = HostName.Parse("sni.example.com"),
            PinSha256 = "AA:BB"
        };

        var result = SubscriptionLinkBuilder.CreateHysteria2ShareUrl(connection, "my device");

        result.Should().Be($"hysteria2://password%20with%20spaces@{uriHost}:443/?insecure=1" +
                           "&pinSHA256=AA%3ABB&obfs=salamander&obfs-password=obfs%26key" +
                           "&sni=sni.example.com&name=my%20device");
    }

    [Fact]
    public void CreateHysteria2ShareUrl_emits_port_hopping_and_local_copy_clears_it() {
        var connection = new Hysteria2ConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            PortBinding = new PortBinding(20000, 30000),
            Password = "password"
        };

        var link = SubscriptionLinkBuilder.CreateHysteria2ShareUrl(connection, "device");
        var local = (Hysteria2ConnectionInfo)SubscriptionLinkBuilder.CreateLocalConnection(connection);

        link.Should().Contain("@[2001:db8::1]:20000/?insecure=1&mport=20000-30000&name=device");
        local.PortBinding.Should().Be(new PortBinding(9000));
        connection.PortBinding.Should().Be(new PortBinding(20000, 30000));
    }

    [Theory]
    [InlineData("vpn.example.com:56000", "vpn.example.com:56000")]
    [InlineData("[2001:db8::1]:443", "[2001:db8::1]:443")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]:443")]
    public void CreateFreeTurnShareUrl_uses_versioned_base64url_payload_and_omits_optional_fields(string peer,
        string expectedPeer) {
        var transport = new FreeTurnConnectionInfo {
            Peer = HostAddress.Parse(peer),
            ObfuscationProfile = null,
            ObfuscationKey = null,
            TurnTransport = "Tcp",
            Mode = 0,
            Streams = 12,
            ClientId = "client-id"
        };

        var result = SubscriptionLinkBuilder.CreateFreeTurnShareUrl(transport, "device", null);
        var encoded = result["freeturn://".Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        using var payload = JsonDocument.Parse(json);

        result.Should().StartWith("freeturn://");
        payload.RootElement.GetProperty("v").GetInt32().Should().Be(1);
        payload.RootElement.GetProperty("provider").GetString().Should().Be("vk");
        payload.RootElement.GetProperty("peer").GetString().Should().Be(expectedPeer);
        payload.RootElement.GetProperty("n").GetInt32().Should().Be(12);
        payload.RootElement.GetProperty("cid").GetString().Should().Be("client-id");
        payload.RootElement.GetProperty("name").GetString().Should().Be("device");
        payload.RootElement.TryGetProperty("key", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("wg", out _).Should().BeFalse();
    }

    [Fact]
    public void CreateNaiveProxyShareUrl_brackets_ipv6_and_local_copy_preserves_tls_options() {
        var connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            ServerPort = 443,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("sni.example.com"),
            PinSha256 = "AA:BB"
        };

        var link = SubscriptionLinkBuilder.CreateNaiveProxyShareUrl(connection, "device");
        var local = (NaiveProxyConnectionInfo)SubscriptionLinkBuilder.CreateLocalConnection(connection);

        link.Should().Be("naive+https://user:password@[2001:db8::1]:443/?sni=sni.example.com" +
                         "&pinSHA256=AA%3ABB&name=device");
        local.ServerAddress.Should().Be(HostName.Parse("127.0.0.1"));
        local.ServerPort.Should().Be(9000);
        local.Sni.Should().Be(connection.Sni);
        local.PinSha256.Should().Be(connection.PinSha256);
        connection.ServerAddress.Should().Be(HostName.Parse("2001:db8::1"));
    }

    [Fact]
    public void Subscription_hosts_round_trip_as_json_strings_with_port_binding() {
        var endpoint = new SubscriptionEndpoint {
            Name = "Main",
            Address = HostAddress.Parse("[2001:db8::1]:8443"),
            Connection = new Hysteria2ConnectionInfo {
                ServerAddress = HostName.Parse("2001:db8::1"),
                PortBinding = new PortBinding(443, 4450),
                Password = "secret",
            }
        };

        var json = JsonSerializer.Serialize(endpoint);
        using var document = JsonDocument.Parse(json);
        var connectionJson = document.RootElement.GetProperty("Connection");
        var restored = JsonSerializer.Deserialize<SubscriptionEndpoint>(json)!;
        var restoredConnection = (Hysteria2ConnectionInfo)restored.Connection;

        document.RootElement.GetProperty("Address").GetString().Should().Be("[2001:db8::1]:8443");
        connectionJson.GetProperty("ServerAddress").GetString().Should().Be("2001:db8::1");
        var bindingJson = connectionJson.GetProperty("PortBinding");
        bindingJson.GetProperty("ListenPort").GetInt32().Should().Be(443);
        bindingJson.GetProperty("PortRangeEnd").GetInt32().Should().Be(4450);
        bindingJson.TryGetProperty("PortRange", out _).Should().BeFalse();
        connectionJson.TryGetProperty("ServerPort", out _).Should().BeFalse();
        connectionJson.TryGetProperty("PortRange", out _).Should().BeFalse();
        connectionJson.GetProperty("Sni").ValueKind.Should().Be(JsonValueKind.Null);
        restored.Address.Should().Be(endpoint.Address);
        restoredConnection.ServerAddress.Should().Be(((Hysteria2ConnectionInfo)endpoint.Connection).ServerAddress);
        restoredConnection.PortBinding.Should().Be(new PortBinding(443, 4450));
        restoredConnection.Sni.Should().BeNull();
    }

    [Fact]
    public void CreateWireGuardConfig_brackets_ipv6_endpoint_and_local_copy_keeps_keys() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            ServerPort = 51820,
            PrivateKey = "private-key",
            AssignedIp = "10.0.0.2",
            ServerPublicKey = "public-key",
            Mtu = 1408,
            IsAmneziaWg = false,
            H1 = "1", H2 = "2", H3 = "3", H4 = "4"
        };

        var config = SubscriptionLinkBuilder.CreateWireGuardConfig(connection);
        var local = (WireGuardConnectionInfo)SubscriptionLinkBuilder.CreateLocalConnection(connection);

        config.Should().Contain("Endpoint = [2001:db8::1]:51820");
        SubscriptionLinkBuilder.CreateWireGuardConfig(local).Should().Contain("Endpoint = 127.0.0.1:9000");
        local.Mtu.Should().Be(1280);
        local.PrivateKey.Should().Be(connection.PrivateKey);
        local.ServerPublicKey.Should().Be(connection.ServerPublicKey);
        connection.Mtu.Should().Be(1408);
    }
}