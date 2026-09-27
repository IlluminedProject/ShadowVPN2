using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Frontend.Api;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionLinkBuilderTests {
    [Fact]
    public void CreateHysteria2ShareUrl_escapes_credentials_and_keeps_connection_options() {
        var connection = new Hysteria2ConnectionInfo {
            ServerAddress = "vpn.example.com",
            ServerPort = 443,
            Password = "password with spaces",
            ObfsType = "salamander",
            ObfsPassword = "obfs&key",
            Sni = "sni.example.com",
            PinSha256 = "AA:BB"
        };

        var result = SubscriptionLinkBuilder.CreateHysteria2ShareUrl(connection, "my device");

        result.Should().Be("hysteria2://password%20with%20spaces@vpn.example.com:443/?insecure=1" +
                           "&pinSHA256=AA%3ABB&obfs=salamander&obfs-password=obfs%26key" +
                           "&sni=sni.example.com&name=my%20device");
    }

    [Fact]
    public void CreateFreeTurnShareUrl_uses_versioned_base64url_payload_and_omits_optional_fields() {
        var transport = new FreeTurnConnectionInfo {
            Peer = "vpn.example.com:56000",
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
        payload.RootElement.GetProperty("peer").GetString().Should().Be(transport.Peer);
        payload.RootElement.GetProperty("n").GetInt32().Should().Be(12);
        payload.RootElement.GetProperty("cid").GetString().Should().Be("client-id");
        payload.RootElement.GetProperty("name").GetString().Should().Be("device");
        payload.RootElement.TryGetProperty("key", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("wg", out _).Should().BeFalse();
    }
}