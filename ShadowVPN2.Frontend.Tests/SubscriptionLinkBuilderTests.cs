using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Frontend.Api;
using Xunit;

namespace ShadowVPN2.Frontend.Tests;

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

        var uri = new Uri(result);
        uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped).Should().Be("password%20with%20spaces");
        uri.Authority.Should().Be($"{uriHost}:443");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.ContainsKey("udp_over_tcp").Should().BeFalse();
        query.ContainsKey("uot").Should().BeFalse();
        query["pinSHA256"].ToString().Should().Be("AA:BB");
        query["obfs"].ToString().Should().Be("salamander");
        query["obfs-password"].ToString().Should().Be("obfs&key");
        query["sni"].ToString().Should().Be("sni.example.com");
        query["name"].ToString().Should().Be("my device");
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

        var uri = new Uri(link);
        uri.Port.Should().Be(20000);
        QueryHelpers.ParseQuery(uri.Query)["mport"].ToString()
            .Should().Be("20000-30000");
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

    [Theory]
    [InlineData(false, "naive+https")]
    [InlineData(true, "naive+quic")]
    public void CreateNaiveProxyShareUrl_brackets_ipv6_and_local_copy_preserves_tls_options(bool quic, string scheme) {
        var connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            ServerPort = 443,
            Quic = quic,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("sni.example.com"),
            PinSha256 = "AA:BB"
        };

        var link = SubscriptionLinkBuilder.CreateNaiveProxyShareUrl(connection, "device");
        var local = (NaiveProxyConnectionInfo)SubscriptionLinkBuilder.CreateLocalConnection(connection);

        var uri = new Uri(link);
        uri.Scheme.Should().Be(scheme);
        uri.Authority.Should().Be("[2001:db8::1]:443");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["sni"].ToString().Should().Be("sni.example.com");
        query["pinSHA256"].ToString().Should().Be("AA:BB");
        query["insecure-concurrency"].ToString().Should().Be("2");
        query["uot"].ToString().Should().Be("1");
        query["udp_over_tcp"].ToString().Should().Be("1");
        local.ServerAddress.Should().Be(HostName.Parse("127.0.0.1"));
        local.ServerPort.Should().Be(9000);
        local.Quic.Should().Be(quic);
        local.Sni.Should().Be(connection.Sni);
        local.PinSha256.Should().Be(connection.PinSha256);
        connection.ServerAddress.Should().Be(HostName.Parse("2001:db8::1"));
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

    [Fact]
    public void CreateWireGuardConfig_puts_awg_parameters_in_interface_section() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = true, Jc = 2, Jmin = 10, Jmax = 50,
            S1 = 34, S2 = 84, S3 = 20, S4 = 23,
            H1 = "264548", H2 = "859751", H3 = "897465", H4 = "859745"
        };

        var config = SubscriptionLinkBuilder.CreateWireGuardConfig(connection);

        config.IndexOf("Jc = 2", StringComparison.Ordinal).Should()
            .BeLessThan(config.IndexOf("[Peer]", StringComparison.Ordinal));
        config.IndexOf("H4 = 859745", StringComparison.Ordinal).Should()
            .BeLessThan(config.IndexOf("[Peer]", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateWireGuardConfig_separates_every_directive_with_newlines() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = true, Jc = 2, Jmin = 10, Jmax = 50,
            S1 = 34, S2 = 84, S3 = 20, S4 = 23,
            H1 = "264548", H2 = "859751", H3 = "897465", H4 = "859745"
        };

        var lines = SubscriptionLinkBuilder.CreateWireGuardConfig(connection)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Should().ContainInOrder(
            "[Interface]", "PrivateKey = private-key", "Address = 10.0.0.2/32", "MTU = 1408", "Jc = 2",
            "Jmin = 10", "Jmax = 50", "S1 = 34", "S2 = 84", "S3 = 20", "S4 = 23", "H1 = 264548",
            "H2 = 859751", "H3 = 897465", "H4 = 859745", "[Peer]", "PublicKey = public-key",
            "Endpoint = vpn.example.com:51820", "AllowedIPs = 0.0.0.0/0", "PersistentKeepalive = 25");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateWireGuardConfig_omits_all_absent_awg_parameters(bool amnezia) {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = amnezia
        };

        var config = SubscriptionLinkBuilder.CreateWireGuardConfig(connection);

        config.Should().Be(string.Join(Environment.NewLine,
            "[Interface]", "PrivateKey = private-key", "Address = 10.0.0.2/32", "MTU = 1408", "",
            "[Peer]", "PublicKey = public-key", "Endpoint = vpn.example.com:51820",
            "AllowedIPs = 0.0.0.0/0", "PersistentKeepalive = 25", ""));
    }

    [Fact]
    public void CreateWireGuardConfig_omits_blank_and_null_awg_parameters_and_retains_zero() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = true,
            Jc = 0, Jmax = 0, S1 = 0, S3 = 0, H1 = "0", H2 = "", H3 = " \t ",
            I1 = "", I2 = " \t ", I3 = "<b 0x01>"
        };

        var config = SubscriptionLinkBuilder.CreateWireGuardConfig(connection);

        config.Should().Be(string.Join(Environment.NewLine,
            "[Interface]", "PrivateKey = private-key", "Address = 10.0.0.2/32", "MTU = 1408",
            "Jc = 0", "Jmax = 0", "S1 = 0", "S3 = 0", "H1 = 0", "I3 = <b 0x01>", "",
            "[Peer]", "PublicKey = public-key", "Endpoint = vpn.example.com:51820",
            "AllowedIPs = 0.0.0.0/0", "PersistentKeepalive = 25", ""));
    }

    [Fact]
    public void Local_wireguard_copy_preserves_absent_parameters_and_explicit_zeros() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = true, Jc = 0, S2 = 0, H4 = "0", I5 = "<b 0x01>"
        };

        var local = (WireGuardConnectionInfo)SubscriptionLinkBuilder.CreateLocalConnection(connection);

        local.Jc.Should().Be(0);
        local.Jmin.Should().BeNull();
        local.Jmax.Should().BeNull();
        local.S1.Should().BeNull();
        local.S2.Should().Be(0);
        local.S3.Should().BeNull();
        local.S4.Should().BeNull();
        local.H1.Should().BeNull();
        local.H2.Should().BeNull();
        local.H3.Should().BeNull();
        local.H4.Should().Be("0");
        local.I5.Should().Be("<b 0x01>");
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateWireGuardShareUrl_uses_throne_wg_scheme_for_all_variants(bool amnezia) {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"),
            ServerPort = 51820,
            PrivateKey = "private-key",
            AssignedIp = "10.0.0.2",
            ServerPublicKey = "public-key",
            Mtu = 1408,
            IsAmneziaWg = amnezia, Jc = 0,
            H1 = "1", H2 = "2", H3 = "3", H4 = "4"
        };

        var link = SubscriptionLinkBuilder.CreateWireGuardShareUrl(connection);

        link.Should().StartWith("wg://vpn.example.com:51820?");
        link.Should().Contain("private_key=private-key");
        link.Should().Contain("local_address=10.0.0.2%2F32");
        link.Should().Contain($"enable_amnezia={amnezia.ToString().ToLowerInvariant()}");
        if (amnezia)
            link.Should().Contain("jc=0");
    }

    [Fact]
    public void CreateWireGuardShareUrl_omits_absent_awg_parameters_and_retains_zero() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"), ServerPort = 51820,
            PrivateKey = "private-key", AssignedIp = "10.0.0.2", ServerPublicKey = "public-key",
            Mtu = 1408, IsAmneziaWg = true, Jc = 0, S2 = 0, H1 = "0"
        };

        var query = QueryHelpers.ParseQuery(new Uri(SubscriptionLinkBuilder.CreateWireGuardShareUrl(connection)).Query);

        query["jc"].ToString().Should().Be("0");
        query["s2"].ToString().Should().Be("0");
        query["h1"].ToString().Should().Be("0");
        query.Keys.Should().NotContain("jmin");
        query.Keys.Should().NotContain("jmax");
        query.Keys.Should().NotContain("s1");
        query.Keys.Should().NotContain("s3");
        query.Keys.Should().NotContain("s4");
        query.Keys.Should().NotContain("h2");
        query.Keys.Should().NotContain("h3");
        query.Keys.Should().NotContain("h4");
    }
}