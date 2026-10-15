using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using Xunit;

namespace ShadowVPN2.Tests;

public class RemoveEmptyInboundsExtensionTests {
    [Fact]
    public async Task ConfigureRemovesOnlyInboundsWithoutUsers() {
        var populated = new Hysteria2InboundConfig {
            Users = [new Hysteria2User { Name = "client", Password = "password" }]
        };
        var config = new SingBoxConfig();
        config.Inbounds.AddRange([new Hysteria2InboundConfig(), new NaiveProxyInboundConfig(), populated]);
        var context = new SingBoxContext(config, new List<EntityClient>(),
            new List<ProtocolGlobalSettings>(), new List<TransportSettings>());
        await using var extension = new RemoveEmptyInboundsExtension();

        await extension.ConfigureAsync(context, CancellationToken.None);
        Assert.Same(populated, Assert.Single(config.Inbounds));
        await extension.ConfigureAsync(context, CancellationToken.None);
        Assert.Same(populated, Assert.Single(config.Inbounds));
    }

    [Fact]
    public async Task ConfigurePreservesEmptyConfiguration() {
        var context = new SingBoxContext(new SingBoxConfig(), [], [], []);
        await using var extension = new RemoveEmptyInboundsExtension();
        await extension.ConfigureAsync(context, CancellationToken.None);
        Assert.Empty(context.Config.Inbounds);
    }
}