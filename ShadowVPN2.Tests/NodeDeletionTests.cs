using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NodeDeletionTests(RavenServerFixture fixture) : IClassFixture<RavenServerFixture>, IAsyncLifetime {
    public Task InitializeAsync() {
        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        return Task.CompletedTask;
    }

    [RavenServerFact]
    public async Task Pending_node_can_be_deleted() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var store = await fixture.CreateStoreAsync(timeout.Token);

        // Create a pending node
        var nodeId = Guid.NewGuid();
        var pendingNode = new EntityClusterNode {
            Id = "EntityClusterNodes|",
            NodeId = nodeId,
            Name = "TestPendingNode",
            Domain = null,
            JoinSecret = Guid.NewGuid()
        };
        using (var session = store.OpenAsyncSession(new SessionOptions {
                   TransactionMode = TransactionMode.ClusterWide
               })) {
            await session.StoreAsync(pendingNode, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        // Verify node exists
        using (var session = store.OpenAsyncSession()) {
            var nodes = await session.Query<EntityClusterNode>().ToListAsync(timeout.Token);
            Assert.Contains(nodes, n => n.NodeId == nodeId);
        }

        // Delete pending node
        using (var session = store.OpenAsyncSession(new SessionOptions {
                   TransactionMode = TransactionMode.ClusterWide
               })) {
            var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>(
                "EntityClusterNodes/", pageSize: int.MaxValue, token: timeout.Token);
            var node = nodes.FirstOrDefault(n => n.NodeId == nodeId);
            Assert.NotNull(node);
            Assert.NotNull(node.JoinSecret); // Verify it's pending

            session.Delete(node);
            await session.SaveChangesAsync(timeout.Token);
        }

        // Verify node deleted
        using (var session = store.OpenAsyncSession()) {
            var nodes = await session.Query<EntityClusterNode>().ToListAsync(timeout.Token);
            Assert.DoesNotContain(nodes, n => n.NodeId == nodeId);
        }
    }

    [RavenServerFact]
    public async Task Pending_node_with_network_status_deletes_status() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var store = await fixture.CreateStoreAsync(timeout.Token);

        var nodeId = Guid.NewGuid();
        var pendingNode = new EntityClusterNode {
            Id = "EntityClusterNodes|",
            NodeId = nodeId,
            Name = "NodeWithStatus",
            JoinSecret = Guid.NewGuid()
        };

        using (var session = store.OpenAsyncSession(new SessionOptions {
                   TransactionMode = TransactionMode.ClusterWide
               })) {
            await session.StoreAsync(pendingNode, timeout.Token);
            var status = new EntityNodeNetworkStatus {
                Id = NodeNetworkService.GetStatusId(nodeId),
                NodeId = nodeId,
                PublicIpv4 = "1.2.3.4"
            };
            await session.StoreAsync(status, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        // Delete node and status
        using (var session = store.OpenAsyncSession(new SessionOptions {
                   TransactionMode = TransactionMode.ClusterWide
               })) {
            var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>(
                "EntityClusterNodes/", pageSize: int.MaxValue, token: timeout.Token);
            var node = nodes.FirstOrDefault(n => n.NodeId == nodeId);
            Assert.NotNull(node);

            session.Delete(node);

            var statusId = NodeNetworkService.GetStatusId(nodeId);
            var status = await session.LoadAsync<EntityNodeNetworkStatus>(statusId, timeout.Token);
            if (status != null)
                session.Delete(status);

            await session.SaveChangesAsync(timeout.Token);
        }

        // Verify both deleted
        using (var session = store.OpenAsyncSession()) {
            var nodes = await session.Query<EntityClusterNode>().ToListAsync(timeout.Token);
            Assert.DoesNotContain(nodes, n => n.NodeId == nodeId);

            var status = await session.LoadAsync<EntityNodeNetworkStatus>(
                NodeNetworkService.GetStatusId(nodeId), timeout.Token);
            Assert.Null(status);
        }
    }
}