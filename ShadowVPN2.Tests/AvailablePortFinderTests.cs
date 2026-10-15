using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Frontend.Api;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class AvailablePortFinderTests {
    [Fact]
    public void Wraps_to_lower_free_port_when_upper_range_is_occupied() {
        Assert.Equal(1, AvailablePortFinder.Find(56000, [],
            [new FreeTurnTransportSettingsDto { PortBinding = new PortBinding(56000, 65535) }], 51820));
    }

    [Fact]
    public void Returns_null_when_all_ports_are_occupied() {
        Assert.Null(AvailablePortFinder.Find(56000,
            [new Hysteria2ProtocolSettingsDto { PortBinding = new PortBinding(1, 65535) }], [], null));
    }

    [Fact]
    public void Skips_reserved_and_single_protocol_ports_after_wrapping() {
        Assert.Equal(3, AvailablePortFinder.Find(65535,
            [new AwgProtocolSettingsDto { ListenPort = 1 }],
            [new FreeTurnTransportSettingsDto { PortBinding = new PortBinding(65535) }], 2));
    }
}