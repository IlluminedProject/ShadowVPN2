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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatesSortClientsByNumericAddress(bool includeAllUsers) {
        using var store = new DocumentStore();
        using var service = new ClientService(store, NullLogger<ClientService>.Instance);
        using var subscription = new ClientService.ClientSubscription(service, "users/owner", 10, includeAllUsers);
        var clients = new[] { 11, 1, 10, 2, 254, 3 }.Select(number => new EntityClient {
            Id = $"Clients/10/{number}", UserId = "users/owner", Name = $"Device {number}"
        }).ToArray();

        await subscription.NotifyAsync(10, clients);
        if (includeAllUsers) {
            await subscription.NotifyAsync(11,
                [new EntityClient { Id = "Clients/11/1", UserId = "users/eleven", Name = "Eleven" }]);
            await subscription.NotifyAsync(2,
                [new EntityClient { Id = "Clients/2/1", UserId = "users/two", Name = "Two" }]);
        }

        var expected = new[] {
            "100.64.10.1", "100.64.10.2", "100.64.10.3", "100.64.10.10",
            "100.64.10.11", "100.64.10.254"
        };
        if (includeAllUsers)
            expected = [.. expected, "100.64.2.1", "100.64.11.1"];
        Assert.Equal(expected,
            (await subscription.GetCurrentClientsAsync()).Select(client => client.GetAssignedIp().ToString()));
    }
}