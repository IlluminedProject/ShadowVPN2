using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using Xunit;

namespace ShadowVPN2.Contracts.Tests;

public sealed class SubscriptionContractSerializationTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Naive_subscription_connection_round_trips_quic_flag(bool quic) {
        ProtocolConnectionInfo connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"),
            ServerPort = 8443,
            Quic = quic,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("sni.example.com")
        };

        var json = JsonSerializer.Serialize(connection);
        var restored = JsonSerializer.Deserialize<ProtocolConnectionInfo>(json);

        restored.Should().BeOfType<NaiveProxyConnectionInfo>().Which.Quic.Should().Be(quic);
    }

    [Fact]
    public void Existing_naive_subscription_connection_defaults_to_https() {
        const string json = """
                            {"type":"naive","ServerAddress":"vpn.example.com","ServerPort":443,
                             "Username":"user","Password":"password","Sni":"vpn.example.com"}
                            """;

        var restored = JsonSerializer.Deserialize<ProtocolConnectionInfo>(json);

        restored.Should().BeOfType<NaiveProxyConnectionInfo>().Which.Quic.Should().BeFalse();
    }

    [Fact]
    public void Subscription_hosts_round_trip_as_json_strings_with_port_binding() {
        var endpoint = new SubscriptionEndpoint {
            Name = "Main",
            Address = HostAddress.Parse("[2001:db8::1]:8443"),
            Connection = new Hysteria2ConnectionInfo {
                ServerAddress = HostName.Parse("2001:db8::1"),
                PortBinding = new PortBinding(443, 4450),
                Password = "secret"
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wireguard_connection_json_preserves_null_and_zero_awg_parameters(bool explicitZero) {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = explicitZero,
            Jc = explicitZero ? 0u : null, Jmin = explicitZero ? 0u : null, Jmax = explicitZero ? 0u : null,
            S1 = explicitZero ? 0 : null, S2 = explicitZero ? 0 : null,
            S3 = explicitZero ? 0 : null, S4 = explicitZero ? 0 : null,
            H1 = explicitZero ? "0" : null, H2 = explicitZero ? "0" : null,
            H3 = explicitZero ? "0" : null, H4 = explicitZero ? "0" : null
        };

        var json = JsonSerializer.Serialize(connection);
        using var document = JsonDocument.Parse(json);
        foreach (var field in new[] { "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4" }) {
            var value = document.RootElement.GetProperty(field);
            if (!explicitZero)
                value.ValueKind.Should().Be(JsonValueKind.Null);
            else if (field.StartsWith('H'))
                value.GetString().Should().Be("0");
            else
                value.GetUInt32().Should().Be(0);
        }

        var restored = JsonSerializer.Deserialize<WireGuardConnectionInfo>(json)!;
        restored.Jc.Should().Be(connection.Jc);
        restored.Jmin.Should().Be(connection.Jmin);
        restored.Jmax.Should().Be(connection.Jmax);
        restored.S1.Should().Be(connection.S1);
        restored.S2.Should().Be(connection.S2);
        restored.S3.Should().Be(connection.S3);
        restored.S4.Should().Be(connection.S4);
        restored.H1.Should().Be(connection.H1);
        restored.H2.Should().Be(connection.H2);
        restored.H3.Should().Be(connection.H3);
        restored.H4.Should().Be(connection.H4);
    }
}