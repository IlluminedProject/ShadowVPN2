using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Subscription;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionPortTests {
    private static readonly Guid Protocol = Guid.Parse("acfcde9e-69ea-4297-b75b-19165d6bb752");

    [Theory]
    [InlineData(null)]
    [InlineData("device-1")]
    public void Subscription_ports_survive_context_recreation_and_host_normalization(string? hwid) {
        var binding = new PortBinding(10000, 65535);
        var first = new SubscriptionGenerationContext(true, hwid).SelectWireGuardPort(
            binding, "Clients/1/2", Protocol, HostName.Parse("VPN.Example.com."));
        var second = new SubscriptionGenerationContext(true, hwid).SelectWireGuardPort(
            binding, "Clients/1/2", Protocol, HostName.Parse("vpn.example.com"));
        Assert.Equal(first, second);
        Assert.InRange(first, 10000, 65535);
    }

    [Fact]
    public void Missing_and_empty_hwid_share_the_same_port() {
        var binding = new PortBinding(1, 65535);
        var host = HostName.Parse("vpn.example.com");
        var expected =
            new SubscriptionGenerationContext(true).SelectWireGuardPort(binding, "Clients/1/2", Protocol, host);
        foreach (var hwid in new[] { "", "   " }) {
            Assert.Equal(expected, new SubscriptionGenerationContext(true, hwid)
                .SelectWireGuardPort(binding, "Clients/1/2", Protocol, host));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Single_port_and_full_range_are_supported(bool stable) {
        var context = new SubscriptionGenerationContext(stable, "device");
        var host = HostName.Parse("::1");
        Assert.Equal(65535, context.SelectWireGuardPort(new PortBinding(65535), "Clients/1/2", Protocol, host));
        for (var i = 0; i < 32; i++) {
            Assert.InRange(context.SelectWireGuardPort(new PortBinding(1, 65535), $"Clients/1/{i}", Protocol, host), 1,
                65535);
        }
    }

    [Fact]
    public void Device_ids_are_unambiguously_framed_and_affect_distribution() {
        var binding = new PortBinding(1, 65535);
        var host = HostName.Parse("vpn.example.com");
        var ports = Enumerable.Range(0, 64).Select(i => new SubscriptionGenerationContext(true, $"device-{i}")
            .SelectWireGuardPort(binding, "Clients/1/2", Protocol, host)).ToHashSet();
        Assert.True(ports.Count > 32);
    }
}