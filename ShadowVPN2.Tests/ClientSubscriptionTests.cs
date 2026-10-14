using Microsoft.Extensions.Logging.Abstractions;
using Raven.Client.Documents;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;
using Xunit;

namespace ShadowVPN2.Tests;

public class ClientSubscriptionTests {
    [Fact]
    public async Task GroupUpdatesPreserveOtherOwnersAndRemoveDeletedClients() {
        using var store = new DocumentStore();
        using var service = new ClientService(store, NullLogger<ClientService>.Instance);
        using var subscription = new ClientService.ClientSubscription(service, "users/admin", 1, true);
        IReadOnlyList<EntityClient> snapshot = [];
        subscription.ClientsUpdated += clients => {
            snapshot = clients;
            return Task.CompletedTask;
        };
        var own = new EntityClient { Id = "Clients/1/1", UserId = "users/admin", Name = "Own" };
        var other = new EntityClient { Id = "Clients/2/1", UserId = "users/member", Name = "Other" };
        await subscription.NotifyAsync(1, [own]);
        await subscription.NotifyAsync(2, [other]);
        Assert.Equal(new[] { own.Id, other.Id }, snapshot.Select(c => c.Id));
        var updated = new EntityClient { Id = other.Id, UserId = other.UserId, Name = "Updated" };
        await subscription.NotifyAsync(2, [updated]);
        Assert.Same(own, snapshot[0]);
        Assert.Equal("Updated", snapshot[1].Name);
        await subscription.NotifyAsync(2, []);
        Assert.Same(own, Assert.Single(snapshot));
        Assert.Same(snapshot, await subscription.GetCurrentClientsAsync());
    }
}