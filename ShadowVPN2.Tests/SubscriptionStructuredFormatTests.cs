using System.Text.Json.Nodes;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Models.Common;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Services;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionStructuredFormatTests {
    [Fact]
    public void Singbox_preserves_hysteria_port_hopping_obfuscation_and_certificate_pin() {
        var connection = new Hysteria2ConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), PortBinding = new PortBinding(20000, 30000),
            Password = "secret", ObfsType = "salamander", ObfsPassword = "mask",
            Sni = HostName.Parse("tls.example.com"), PinSha256 = new string('A', 64)
        };
        var json = JsonNode.Parse(new SubscriptionFormatService().Render(Response(connection), SubscriptionFormat.SingBox))!;
        var outbound = json["outbounds"]![1]!;
        Assert.Equal("20000:30000", outbound["server_ports"]![0]!.GetValue<string>());
        Assert.Null(outbound["server_port"]);
        Assert.Equal("secret", outbound["password"]!.GetValue<string>());
        Assert.Equal("mask", outbound["obfs"]!["password"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Convert.FromHexString(new string('A', 64))),
            outbound["tls"]!["certificate_sha256"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Wireguard_exports_as_singbox_endpoint_and_xray_peer_without_losing_keys() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"), ServerPort = 51820,
            IsAmneziaWg = false, H1 = "1", H2 = "2", H3 = "3", H4 = "4",
            PrivateKey = "private", ServerPublicKey = "public", AssignedIp = "10.0.0.2", Mtu = 1380
        };
        var service = new SubscriptionFormatService();
        var singbox = JsonNode.Parse(service.Render(Response(connection), SubscriptionFormat.SingBox))!;
        Assert.Equal("private", singbox["endpoints"]![0]!["private_key"]!.GetValue<string>());
        Assert.Equal("10.0.0.2/32", singbox["endpoints"]![0]!["address"]![0]!.GetValue<string>());
        var xray = JsonNode.Parse(service.Render(Response(connection), SubscriptionFormat.XrayJson))!;
        Assert.Equal("public", xray["outbounds"]![0]!["settings"]!["peers"]![0]!["publicKey"]!.GetValue<string>());
        Assert.Equal("[2001:db8::1]:51820", xray["outbounds"]![0]!["settings"]!["peers"]![0]!["endpoint"]!.GetValue<string>());
        connection.IsAmneziaWg = true;
        connection.Jc = 4;
        connection.Jmin = 40;
        connection.Jmax = 70;
        connection.S1 = 12;
        connection.S2 = 24;
        connection.S3 = 36;
        connection.S4 = 48;
        connection.I1 = "<b 0x1234>";
        var awg = JsonNode.Parse(service.Render(Response(connection), SubscriptionFormat.SingBox))!["endpoints"]![0]!;
        Assert.Equal("awg", awg["type"]!.GetValue<string>());
        Assert.Equal("private", awg["private_key"]!.GetValue<string>());
        Assert.Equal("10.0.0.2/32", awg["address"]![0]!.GetValue<string>());
        Assert.Equal("public", awg["peers"]![0]!["public_key"]!.GetValue<string>());
        Assert.Equal("2001:db8::1", awg["peers"]![0]!["address"]!.GetValue<string>());
        Assert.Equal(51820, awg["peers"]![0]!["port"]!.GetValue<int>());
        Assert.Equal(4u, awg["jc"]!.GetValue<uint>());
        Assert.Equal(40u, awg["jmin"]!.GetValue<uint>());
        Assert.Equal(70u, awg["jmax"]!.GetValue<uint>());
        Assert.Equal(12, awg["s1"]!.GetValue<int>());
        Assert.Equal(48, awg["s4"]!.GetValue<int>());
        Assert.Equal("1", awg["h1"]!.GetValue<string>());
        Assert.Equal("4", awg["h4"]!.GetValue<string>());
        Assert.Equal("<b 0x1234>", awg["i1"]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => service.Render(Response(connection), SubscriptionFormat.XrayJson));
    }

    private static SubscriptionResponse Response(ProtocolConnectionInfo connection) => new() {
        ClientName = "device", Protocols = [new ProtocolSubscription {
            Protocol = "test", Endpoints = [new SubscriptionEndpoint {
                Name = "Main", Address = HostAddress.Parse("vpn.example.com"), Connection = connection
            }]
        }]
    };
}
