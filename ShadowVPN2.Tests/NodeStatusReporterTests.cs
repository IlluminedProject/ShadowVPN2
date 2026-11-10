using DnsClient;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
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
        using (var session = store.OpenAsyncSession()) {
            await session.StoreAsync(new EntityClusterNode {
                Id = "ClusterNodes/1", NodeId = nodeId, Name = "Local"
            }, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        var dnsCheck = CreateDnsCheck(store, options);
        using var reporter = new NodeStatusReporter(store, options, [check], updater, dnsCheck,
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

            using var restarted = new NodeStatusReporter(store, options, [], updater, dnsCheck,
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

    [RavenServerFact]
    public async Task Dns_worker_publishes_snapshot_and_replaces_failed_result_without_retaining_fresh_success() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        var options = Options.Create(new LocalConfiguration { NodeId = nodeId });
        var networkCheckedAt = DateTimeOffset.UtcNow;
        const string address = "203.0.113.10";
        using (var session = store.OpenAsyncSession()) {
            await session.StoreAsync(new EntityClusterNode {
                Id = "ClusterNodes/1", NodeId = nodeId, Name = "Local", Domain = HostName.Parse(address)
            }, timeout.Token);
            await session.StoreAsync(new EntityNodeNetworkStatus {
                Id = NodeNetworkService.GetStatusId(nodeId), NodeId = nodeId,
                PublicIpv4 = address, CheckedAt = networkCheckedAt
            }, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        var updater = new UpdaterClient(Options.Create(new UpdaterOptions()), NullLogger<UpdaterClient>.Instance);
        using var reporter = new NodeStatusReporter(store, options, [], updater, CreateDnsCheck(store, options),
            NullLogger<NodeStatusReporter>.Instance);
        await reporter.StartAsync(timeout.Token);
        try {
            EntityNodeRuntimeState success;
            while (true) {
                success = await WaitForReportAsync(reporter, nodeId, timeout.Token);
                if (success.DomainDns is not null)
                    break;
                await Task.Delay(50, timeout.Token);
            }

            Assert.True(success.DomainDns.MatchesNode);
            Assert.Equal(nodeId, success.DomainDns.ObserverNodeId);
            Assert.Equal(networkCheckedAt, success.DomainDns.NetworkCheckedAt);
            Assert.Equal(address, success.DomainDns.ExpectedIpv4);
            Assert.Equal(NodeCheckStatus.Healthy, success.Checks[NodeDomainDnsCheck.CheckId].Status);

            using (var session = store.OpenAsyncSession()) {
                session.Delete("ClusterNodes/1");
                await session.SaveChangesAsync(timeout.Token);
            }

            while (true) {
                var failure = await WaitForReportAsync(reporter, nodeId, timeout.Token);
                if (failure.DomainDns is null &&
                    failure.Checks.TryGetValue(NodeDomainDnsCheck.CheckId, out var dns)) {
                    Assert.Equal(NodeCheckStatus.Unknown, dns.Status);
                    Assert.True(dns.CheckedAt >= success.DomainDns.CheckedAt);
                    break;
                }

                await Task.Delay(50, timeout.Token);
            }
        }
        finally {
            await reporter.StopAsync(timeout.Token);
        }
    }

    [RavenServerTheory]
    [InlineData(null)]
    [InlineData("Network discovery failed")]
    public async Task Dns_check_reuses_fresh_snapshot_for_unchanged_inputs(string? networkError) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        await StoreDnsInputsAsync(store, nodeId, DateTimeOffset.UtcNow, networkError, timeout.Token);
        var check = CreateDnsCheck(store, Options.Create(new LocalConfiguration { NodeId = nodeId }));

        var first = await check.CheckAsync(timeout.Token);
        var second = await check.CheckAsync(timeout.Token);

        Assert.NotNull(first);
        Assert.True(first.ValidUntil > DateTimeOffset.UtcNow);
        Assert.Same(first, second);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckAsync(canceled.Token));
        using var session = store.OpenAsyncSession();
        Assert.Null(await session.LoadAsync<EntityNodeRuntimeState>(NodeStatusReporter.GetStateId(nodeId),
            timeout.Token));
    }

    [RavenServerTheory]
    [InlineData("domain")]
    [InlineData("ipv4")]
    [InlineData("ipv6")]
    [InlineData("checkedAt")]
    [InlineData("error")]
    [InlineData("errorDetails")]
    [InlineData("checkedAtWithError")]
    [InlineData("networkRemoved")]
    [InlineData("domainRemoved")]
    public async Task Dns_check_rechecks_fresh_snapshot_when_inputs_change(string changedInput) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        var networkError = changedInput is "errorDetails" or "checkedAtWithError" ? "Initial failure" : null;
        await StoreDnsInputsAsync(store, nodeId, DateTimeOffset.UtcNow, networkError, timeout.Token);
        var check = CreateDnsCheck(store, Options.Create(new LocalConfiguration { NodeId = nodeId }));
        var first = await check.CheckAsync(timeout.Token);
        Assert.NotNull(first);
        Assert.True(first.ValidUntil > DateTimeOffset.UtcNow);

        using (var session = store.OpenAsyncSession()) {
            var node = await session.LoadAsync<EntityClusterNode>("ClusterNodes/1", timeout.Token);
            var network = await session.LoadAsync<EntityNodeNetworkStatus>(NodeNetworkService.GetStatusId(nodeId),
                timeout.Token);
            switch (changedInput) {
                case "domain": node.Domain = HostName.Parse("203.0.113.11"); break;
                case "ipv4": network.PublicIpv4 = "203.0.113.11"; break;
                case "ipv6": network.PublicIpv6 = "2001:db8::1"; break;
                case "checkedAt":
                case "checkedAtWithError": network.CheckedAt = network.CheckedAt.AddSeconds(1); break;
                case "error":
                case "errorDetails": network.LastError = "New network failure"; break;
                case "networkRemoved": session.Delete(network); break;
                case "domainRemoved": node.Domain = null; break;
            }

            await session.SaveChangesAsync(timeout.Token);
        }

        var second = await check.CheckAsync(timeout.Token);

        Assert.NotSame(first, second);
        if (changedInput == "domainRemoved") {
            Assert.Null(second);
            return;
        }

        Assert.NotNull(second);
        Assert.Equal(nodeId, second.ObserverNodeId);
        switch (changedInput) {
            case "domain": Assert.Equal(HostName.Parse("203.0.113.11"), second.Domain); break;
            case "ipv4": Assert.Equal("203.0.113.11", second.ExpectedIpv4); break;
            case "ipv6": Assert.Equal("2001:db8::1", second.ExpectedIpv6); break;
            case "checkedAt": Assert.Equal(first.NetworkCheckedAt?.AddSeconds(1), second.NetworkCheckedAt); break;
            case "error":
            case "errorDetails":
            case "checkedAtWithError":
            case "networkRemoved": Assert.Equal(DomainValidationState.NetworkStateStale, second.State); break;
        }
    }

    [RavenServerFact]
    public async Task Dns_check_rechecks_expired_snapshot_for_unchanged_inputs() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        var networkCheckedAt = DateTimeOffset.UtcNow - NodeNetworkService.NetworkStateMaxAge - TimeSpan.FromSeconds(1);
        await StoreDnsInputsAsync(store, nodeId, networkCheckedAt, null, timeout.Token);
        var check = CreateDnsCheck(store, Options.Create(new LocalConfiguration { NodeId = nodeId }));

        var first = await check.CheckAsync(timeout.Token);
        var second = await check.CheckAsync(timeout.Token);

        Assert.NotNull(first);
        Assert.True(first.ValidUntil <= DateTimeOffset.UtcNow);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(DomainValidationState.NetworkStateStale, second.State);
        Assert.Equal(networkCheckedAt, second.NetworkCheckedAt);
    }

    private static async Task StoreDnsInputsAsync(IDocumentStore store, Guid nodeId,
        DateTimeOffset checkedAt, string? networkError, CancellationToken cancellationToken) {
        using var session = store.OpenAsyncSession();
        await session.StoreAsync(new EntityClusterNode {
            Id = "ClusterNodes/1", NodeId = nodeId, Name = "Local", Domain = HostName.Parse("203.0.113.10")
        }, cancellationToken);
        await session.StoreAsync(new EntityNodeNetworkStatus {
            Id = NodeNetworkService.GetStatusId(nodeId), NodeId = nodeId,
            PublicIpv4 = "203.0.113.10", CheckedAt = checkedAt, LastError = networkError
        }, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private static NodeDomainDnsCheck CreateDnsCheck(IDocumentStore store, IOptions<LocalConfiguration> options) {
        var network = new NodeNetworkService(store, new TestHttpClientFactory(), new TestServer(), options,
            NullLogger<NodeNetworkService>.Instance);
        var validation = new DomainValidationService(new LookupClient(),
            NullLogger<DomainValidationService>.Instance);
        return new NodeDomainDnsCheck(store, options, network, validation);
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

    private sealed class TestHttpClientFactory : IHttpClientFactory {
        public HttpClient CreateClient(string name) {
            return new HttpClient();
        }
    }

    private sealed class TestServer : IServer {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }

        public void Dispose() { }
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