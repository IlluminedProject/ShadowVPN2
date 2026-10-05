using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Frontend.Api;
using ShadowVPN2.Infrastructure;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NaiveProxyTests {
    [Fact]
    public void Naive_protocol_settings_round_trip_and_create_certificate_pin() {
        var settings = new NaiveProxyGlobalSettings { Id = Guid.NewGuid() };
        settings.GenerateSelfSignedCertificate();

        var json = JsonSerializer.Serialize<ProtocolGlobalSettings>(settings, DataUtils.DefaultSerializerOptions);
        var restored = JsonSerializer.Deserialize<ProtocolGlobalSettings>(json, DataUtils.DefaultSerializerOptions);

        var restoredNaive = restored.Should().BeOfType<NaiveProxyGlobalSettings>().Subject;
        restoredNaive.TlsCertificatePem.Should().NotBeNullOrWhiteSpace();
        restoredNaive.TlsKeyPem.Should().NotBeNullOrWhiteSpace();
        restoredNaive.GetCertificateFingerprint().Should().MatchRegex("^([0-9A-F]{2}:){31}[0-9A-F]{2}$");
        NaiveProxyGlobalSettings.SocketKind.Should().Be(ProtocolSocketKind.Tcp);
    }

    [Fact]
    public void Naive_inbound_serializes_as_tcp_with_tls_and_users() {
        var config = new NaiveProxyInboundConfig {
            Tag = "naive-443",
            Listen = "0.0.0.0",
            ListenPort = 443,
            Users = [new NaiveProxyUser { Username = "user", Password = "secret" }],
            Tls = new InboundTlsConfig {
                Enabled = true,
                ServerName = "shadowvpn.local",
                Certificate = ["certificate"],
                Key = ["private-key"]
            }
        };

        var json = JsonSerializer.Serialize<InboundConfig>(config, SingBoxService.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be("naive");
        root.GetProperty("network").GetString().Should().Be("tcp");
        root.GetProperty("users")[0].GetProperty("username").GetString().Should().Be("user");
        root.GetProperty("tls").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Naive_share_url_includes_insecure_and_certificate_pin_when_domain_is_absent() {
        var connection = new NaiveProxyConnectionInfo {
            ServerAddress = "203.0.113.10",
            ServerPort = 443,
            Username = "user",
            Password = "pass word",
            Sni = "shadowvpn.local",
            Insecure = true,
            PinSha256 = "AA:BB"
        };

        var link = SubscriptionLinkBuilder.CreateNaiveProxyShareUrl(connection, "my device");

        link.Should().Be("naive+https://user:pass%20word@203.0.113.10:443/?sni=shadowvpn.local" +
                         "&insecure=1&pinSHA256=AA%3ABB&name=my%20device");
    }

    [Fact]
    public void Naive_protocol_dto_round_trips_with_polymorphic_discriminator() {
        var request = new UpdateProtocolsSettingsRequest {
            Protocols = [new NaiveProxyProtocolSettingsDto { Id = Guid.NewGuid(), ListenPort = 443, Enabled = true }]
        };

        var restored = JsonSerializer.Deserialize<UpdateProtocolsSettingsRequest>(JsonSerializer.Serialize(request));

        restored.Should().NotBeNull();
        restored!.Protocols.Should().ContainSingle().Which.Should().BeOfType<NaiveProxyProtocolSettingsDto>();
    }
}