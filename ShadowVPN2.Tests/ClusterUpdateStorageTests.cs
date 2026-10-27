using AwesomeAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Documents.Session;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.Migrations;
using ShadowVPN2.Exceptions;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ClusterUpdateStorageTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string HistoryPrefix = "ShadowVPN2/ClusterUpdate/History/";

    [RavenServerFact]
    public async Task Migration_preserves_every_state_field_removes_legacy_key_and_keeps_history() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var legacy = CreateState();
        var history = new ApplicationUpdateHistoryResponse {
            Id = Guid.NewGuid(), ImageDigest = OtherDigest, Status = "Completed",
            StartedAt = legacy.StartedAt.AddDays(-2), CompletedAt = legacy.StartedAt.AddDays(-1)
        };
        var seeded = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<ClusterUpdateState>(
            ClusterUpdateDocument.DocumentId, legacy, 0), token: timeout.Token);
        Assert.True(seeded.Successful);
        var historyResult = await store.Operations.SendAsync(
            new PutCompareExchangeValueOperation<ApplicationUpdateHistoryResponse>(
                HistoryPrefix + history.Id, history, 0), token: timeout.Token);
        Assert.True(historyResult.Successful);

        await MigrateAsync(store, timeout.Token);

        var migrated = await LoadStateAsync(store, timeout.Token);
        migrated.Should().BeEquivalentTo(legacy);
        Assert.Equal(legacy.Id, (await new ClusterUpdateService(store).GetAsync(timeout.Token))!.Id);
        Assert.Null(await store.Operations.SendAsync(new GetCompareExchangeValueOperation<ClusterUpdateState>(
            ClusterUpdateDocument.DocumentId), token: timeout.Token));
        var retainedHistory = await store.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ApplicationUpdateHistoryResponse>(HistoryPrefix + history.Id),
            token: timeout.Token);
        Assert.NotNull(retainedHistory);
        Assert.Equal(historyResult.Index, retainedHistory.Index);
        retainedHistory.Value.Should().BeEquivalentTo(history);
        (await new ClusterUpdateService(store).GetHistoryAsync(timeout.Token)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(history);

        await MigrateAsync(store, timeout.Token);
        (await LoadStateAsync(store, timeout.Token)).Should().BeEquivalentTo(legacy);
    }

    [RavenServerFact]
    public async Task Migration_rejects_conflicting_document_without_losing_either_update() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var legacy = CreateState();
        var existing = CreateState();
        await StoreStateAsync(store, existing, timeout.Token);
        var seeded = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<ClusterUpdateState>(
            ClusterUpdateDocument.DocumentId, legacy, 0), token: timeout.Token);
        Assert.True(seeded.Successful);

        using (var session =
               store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide })) {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ClusterUpdateDocumentMigration().ApplyAsync(session, timeout.Token));
            Assert.Contains("both", exception.Message);
        }

        (await LoadStateAsync(store, timeout.Token)).Should().BeEquivalentTo(existing);
        var retained = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<ClusterUpdateState>(
            ClusterUpdateDocument.DocumentId), token: timeout.Token);
        Assert.NotNull(retained);
        Assert.Equal(seeded.Index, retained.Index);
        retained.Value.Should().BeEquivalentTo(legacy);
    }

    [RavenServerFact]
    public async Task Concurrent_starts_have_one_winner_for_new_and_completed_updates() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        foreach (var replacePrevious in new[] { false, true }) {
            using var store = await server.CreateStoreAsync(timeout.Token);
            ClusterUpdateState? previous = null;
            if (replacePrevious) {
                previous = CreateState();
                previous.Status = ClusterUpdateStatus.Completed;
                previous.CurrentNodeIndex = previous.Nodes.Count;
                foreach (var node in previous.Nodes)
                    node.Status = ClusterUpdateNodeStatus.Completed;
                await StoreStateAsync(store, previous, timeout.Token);
            }

            var service = new ClusterUpdateService(store);
            var topology = await store.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(), timeout.Token);
            using var writes = new Barrier(2);
            var writeCount = 0;
            store.OnBeforeStore += (_, _) => {
                if (Interlocked.Increment(ref writeCount) <= 2 &&
                    !writes.SignalAndWait(TimeSpan.FromSeconds(20), timeout.Token))
                    throw new TimeoutException("Both starts must load the same state before either writes");
            };
            var starts = await Task.WhenAll(StartAsync(Digest), StartAsync(OtherDigest));
            var winner = Assert.Single(starts, result => result.Update is not null).Update!;
            var rejected = Assert.Single(starts, result => result.Error is not null).Error!;
            Assert.Equal(409, rejected.StatusCode);
            var current = await service.GetAsync(timeout.Token);
            Assert.NotNull(current);
            Assert.Equal(winner.Id, current.Id);
            Assert.Equal(winner.ImageDigest, current.ImageDigest);
            Assert.Equal("Preparing", current.Status);
            Assert.Equal(2, writeCount);
            Assert.Null(await store.Operations.SendAsync(new GetCompareExchangeValueOperation<ClusterUpdateState>(
                ClusterUpdateDocument.DocumentId), token: timeout.Token));
            var history = await service.GetHistoryAsync(timeout.Token);
            if (previous is null) {
                Assert.Empty(history);
            }
            else {
                var archived = Assert.Single(history);
                Assert.Equal(previous.Id, archived.Id);
                Assert.Equal(previous.ImageDigest, archived.ImageDigest);
                Assert.Equal("Completed", archived.Status);
            }

            Task<(ClusterUpdateResponse? Update, AppException? Error)> StartAsync(string digest) {
                return Task.Run(
                    async () => {
                        try {
                            return (await service.StartAsync(new StartClusterUpdateRequest {
                                BackwardCompatible = true, ImageDigest = digest
                            }, topology, topology.NodeTag, timeout.Token), (AppException?)null);
                        }
                        catch (AppException exception) {
                            return (null, exception);
                        }
                    }, timeout.Token);
            }
        }
    }

    [RavenServerFact]
    public async Task Concurrent_node_preparations_retry_without_losing_either_transition() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var state = CreateState();
        state.Status = ClusterUpdateStatus.Preparing;
        state.CurrentNodeIndex = 0;
        foreach (var node in state.Nodes) {
            node.Status = ClusterUpdateNodeStatus.Preparing;
            node.Error = null;
        }

        await StoreStateAsync(store, state, timeout.Token);
        using var writes = new Barrier(2);
        var writeCount = 0;
        store.OnBeforeStore += (_, _) => {
            if (Interlocked.Increment(ref writeCount) <= 2 &&
                !writes.SignalAndWait(TimeSpan.FromSeconds(20), timeout.Token))
                throw new TimeoutException("Both preparations must load the same state before either writes");
        };
        var service = new ClusterUpdateService(store);

        var prepared = await Task.WhenAll(state.Nodes.Select(node => Task.Run(() =>
            service.PrepareLocalAsync(node.NodeTag, state.Id, true, null, timeout.Token), timeout.Token)));

        Assert.All(prepared, result => Assert.NotNull(result));
        Assert.True(writeCount >= 3, "One conflicting write must retry its transition");
        var response = await service.GetAsync(timeout.Token);
        Assert.NotNull(response);
        Assert.Equal(state.Id, response.Id);
        Assert.Equal("Running", response.Status);
        Assert.All(response.Nodes, node => Assert.Equal("Prepared", node.Status));
        var persisted = await LoadStateAsync(store, timeout.Token);
        Assert.All(persisted.Nodes, node => Assert.Equal(ClusterUpdateNodeStatus.Prepared, node.Status));
        Assert.Equal(state.StartedAt, persisted.StartedAt);
    }

    [RavenServerFact]
    public async Task Concurrent_reconciliation_failures_accumulate_instead_of_overwriting_each_other() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var state = CreateState();
        state.Status = ClusterUpdateStatus.Running;
        state.CurrentNodeIndex = 1;
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Completed;
        state.Nodes[1].Status = ClusterUpdateNodeStatus.Claimed;
        state.Nodes[1].ReconciliationFailures = 0;
        await StoreStateAsync(store, state, timeout.Token);
        using var writes = new Barrier(2);
        var writeCount = 0;
        store.OnBeforeStore += (_, _) => {
            if (Interlocked.Increment(ref writeCount) <= 2 &&
                !writes.SignalAndWait(TimeSpan.FromSeconds(20), timeout.Token))
                throw new TimeoutException("Both failures must load the same state before either writes");
        };
        var service = new ClusterUpdateService(store);

        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                service.RecordReconciliationFailureAsync(state.Id, "B", "Updater unavailable", 2, timeout.Token),
            timeout.Token)));

        Assert.True(writeCount >= 3, "One conflicting write must retry its increment");
        var response = await service.GetAsync(timeout.Token);
        Assert.NotNull(response);
        Assert.Equal("Failed", response.Status);
        Assert.Contains("2 attempts", response.Error);
        Assert.Equal("Completed", response.Nodes[0].Status);
        Assert.Equal("Failed", response.Nodes[1].Status);
        Assert.Equal(2, (await LoadStateAsync(store, timeout.Token)).Nodes[1].ReconciliationFailures);
    }

    [RavenServerFact]
    public async Task Service_state_writes_publish_changes_for_the_fixed_document_id() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        using var changes = store.Changes();
        var observable = changes.ForDocument(ClusterUpdateDocument.DocumentId);
        var notification = new TaskCompletionSource<DocumentChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = observable.Subscribe(change => {
            if (change.Type == DocumentChangeTypes.Put)
                notification.TrySetResult(change);
        }, exception => notification.TrySetException(exception));
        await changes.EnsureConnectedNow().WaitAsync(timeout.Token);
        await observable.EnsureSubscribedNow().WaitAsync(timeout.Token);
        var service = new ClusterUpdateService(store);
        var topology = await store.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(), timeout.Token);

        var started = await service.StartAsync(new StartClusterUpdateRequest {
            BackwardCompatible = true, ImageDigest = Digest
        }, topology, topology.NodeTag, timeout.Token);

        var change = await notification.Task.WaitAsync(timeout.Token);
        Assert.Equal(ClusterUpdateDocument.DocumentId, change.Id);
        Assert.Equal(started.Id, (await service.GetAsync(timeout.Token))!.Id);
        notification = new TaskCompletionSource<DocumentChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        await service.CancelAsync(started.Id, timeout.Token);
        Assert.Equal(ClusterUpdateDocument.DocumentId, (await notification.Task.WaitAsync(timeout.Token)).Id);
        Assert.Equal("Cancelled", (await service.GetAsync(timeout.Token))!.Status);
    }

    private static async Task MigrateAsync(IDocumentStore store, CancellationToken cancellationToken) {
        using var session = store.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        await new ClusterUpdateDocumentMigration().ApplyAsync(session, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private static async Task StoreStateAsync(IDocumentStore store, ClusterUpdateState state,
        CancellationToken cancellationToken) {
        using var session = store.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        await session.StoreAsync(new ClusterUpdateDocument { State = state }, ClusterUpdateDocument.DocumentId,
            cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ClusterUpdateState> LoadStateAsync(IDocumentStore store,
        CancellationToken cancellationToken) {
        using var session = store.OpenAsyncSession();
        var document =
            await session.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId, cancellationToken);
        Assert.NotNull(document);
        return document.State;
    }

    private static ClusterUpdateState CreateState() {
        var started = new DateTimeOffset(2026, 9, 4, 10, 11, 12, TimeSpan.FromHours(2));
        return new ClusterUpdateState {
            Id = Guid.NewGuid(), ImageDigest = Digest, CurrentNodeIndex = 1,
            Status = ClusterUpdateStatus.Running, Error = "Retained rollout detail",
            StartedAt = started, UpdatedAt = started.AddMinutes(9),
            Nodes = [
                new ClusterUpdateNodeState {
                    NodeTag = "A", Status = ClusterUpdateNodeStatus.Completed, ReconciliationFailures = 1,
                    ClaimedAt = started.AddMinutes(1), CompletedAt = started.AddMinutes(4),
                    Error = "Retained node detail"
                },
                new ClusterUpdateNodeState {
                    NodeTag = "B", Status = ClusterUpdateNodeStatus.Claimed, ReconciliationFailures = 2,
                    ClaimedAt = started.AddMinutes(5), CompletedAt = null, Error = "Retained retry detail"
                }
            ]
        };
    }
}