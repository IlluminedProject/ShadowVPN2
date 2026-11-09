using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NodeStatusReporterTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    [RavenServerFact]
    public async Task Blocked_check_does_not_block_reporting_and_restart_discards_old_results() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        var check = new BlockingCheck();
        var options = Options.Create(new LocalConfiguration { NodeId = nodeId });
        var updater = new UpdaterClient(Options.Create(new UpdaterOptions()), NullLogger<UpdaterClient>.Instance);
        using var reporter = new NodeStatusReporter(store, options, [check], updater,
            NullLogger<NodeStatusReporter>.Instance);
        await reporter.StartAsync(timeout.Token);
        try {
            await check.Started.Task.WaitAsync(timeout.Token);
            var state = await WaitForReportAsync(reporter, nodeId, timeout.Token);
            Assert.Empty(state.Checks);
            Assert.True(state.HeartbeatAt >= state.StartedAt);
            await reporter.StopAsync(timeout.Token);

            using (var session = store.OpenAsyncSession()) {
                var old = await session.LoadAsync<EntityNodeRuntimeState>(
                    NodeStatusReporter.GetStateId(nodeId), timeout.Token);
                old.Checks["old-process"] = new NodeCheckResult {
                    Status = NodeCheckStatus.Healthy, CheckedAt = state.HeartbeatAt,
                    ValidUntil = state.HeartbeatAt.AddHours(1)
                };
                await session.SaveChangesAsync(timeout.Token);
            }

            using var restarted = new NodeStatusReporter(store, options, [], updater,
                NullLogger<NodeStatusReporter>.Instance);
            await restarted.StartAsync(timeout.Token);
            try {
                var next = await WaitForReportAsync(restarted, nodeId, timeout.Token, state.InstanceId);
                Assert.NotEqual(state.InstanceId, next.InstanceId);
                Assert.Empty(next.Checks);
            }
            finally {
                await restarted.StopAsync(timeout.Token);
            }
        }
        finally {
            await reporter.StopAsync(timeout.Token);
        }
    }

    private static async Task<EntityNodeRuntimeState> WaitForReportAsync(
        NodeStatusReporter reporter, Guid nodeId, CancellationToken token, string? previousInstance = null) {
        while (true) {
            var reports = await reporter.GetStatesAsync([nodeId], token);
            if (reports.TryGetValue(nodeId, out var report) && report.InstanceId != previousInstance)
                return report;
            await Task.Delay(50, token);
        }
    }

    private sealed class BlockingCheck : INodeStatusCheck {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id {
            get => "blocked";
        }

        public async Task<NodeCheckResult> CheckAsync(CancellationToken cancellationToken) {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}