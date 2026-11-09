using System.Reflection;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NodeAllocationTests(RavenServerFixture fixture) : IClassFixture<RavenServerFixture> {
    [RavenServerFact]
    public async Task Allocation_reuses_deleted_addresses_and_reserves_pending_nodes() {
        using var store = await fixture.CreateStoreAsync();
        await SeedAsync(store, 1, 3);
        var node = await ReserveAsync(store);
        Assert.Equal(2, node.Number);
        Assert.Equal("100.64.0.12", node.AwgMeshIp);
        Assert.NotNull(node.JoinSecret);
        Assert.Equal(4, (await ReserveAsync(store)).Number);
        using (var session =
               store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide })) {
            var removed = await session.LoadAsync<EntityClusterNode>(node.Id);
            session.Delete(removed);
            await session.SaveChangesAsync();
        }

        Assert.Equal(2, (await ReserveAsync(store)).Number);
    }

    private static async Task SeedAsync(IDocumentStore store, params int[] numbers) {
        using var session = store.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        foreach (var number in numbers) {
            await session.StoreAsync(new EntityClusterNode {
                Id = $"EntityClusterNodes/{number}", NodeId = Guid.NewGuid(), Name = $"Node {number}"
            });
        }

        await session.SaveChangesAsync();
    }

    private static Task<EntityClusterNode> ReserveAsync(IDocumentStore store) {
        var service = new ClusterService(store, null!, null!, null!, null!, null!, null!);
        return (Task<EntityClusterNode>)typeof(ClusterService)
            .GetMethod("ReserveNodeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, ["New node", null])!;
    }
}