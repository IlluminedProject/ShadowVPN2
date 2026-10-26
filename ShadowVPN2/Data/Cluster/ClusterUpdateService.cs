using System.Text.RegularExpressions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Http;
using Raven.Client.Exceptions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Exceptions;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateService(IDocumentStore documentStore) {
    private const string StateKey = "ShadowVPN2/ClusterUpdate/State";
    private const string HistoryPrefix = "ShadowVPN2/ClusterUpdate/History/";
    private static readonly Regex DigestPattern = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public async Task RecordReconciliationFailureAsync(Guid updateId, string localTag, string error,
        int maxAttempts, CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { } state || state.Id != updateId ||
                state.Status is not (ClusterUpdateStatus.Preparing or ClusterUpdateStatus.Running)) return;
            var node = state.Nodes.FirstOrDefault(node => node.NodeTag == localTag);
            if (node is null || node.Status is not (ClusterUpdateNodeStatus.Preparing or
                ClusterUpdateNodeStatus.Prepared or ClusterUpdateNodeStatus.Pending or ClusterUpdateNodeStatus.Claimed)) return;
            var updated = ClusterUpdateTransitions.RecordReconciliationFailure(state, localTag, error, maxAttempts);
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, updated, current.Index),
                token: cancellationToken);
            if (result.Successful) return;
        }
    }

    public async Task<ClusterUpdateResponse> StartAsync(StartClusterUpdateRequest request,
        ClusterTopologyResponse topology, string localNodeTag, CancellationToken cancellationToken) {
        if (!UpdateSafetyPolicy.AllowsRollingUpdate(request.BackwardCompatible)) {
            throw new AppException(StatusCodes.Status409Conflict,
                "Rolling update requires a backward-compatible schema change; deploy an expand-and-contract release first");
        }

        if (!DigestPattern.IsMatch(request.ImageDigest))
            throw new ArgumentException("ImageDigest must be a lowercase sha256 digest", nameof(request));

        var votingMembers = topology.Topology.Members;
        int connectedMembers;
        try {
            connectedMembers = await GetConnectedMembersAsync(topology, cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or TimeoutException or
                OperationCanceledException or AllTopologyNodesDownException
                or RavenException { InnerException: HttpRequestException or TimeoutException }) {
            throw new AppException(StatusCodes.Status409Conflict,
                $"Cannot start cluster update: RavenDB leader '{topology.Leader}' is unreachable");
        }

        if (!HasQuorum(votingMembers.Count, connectedMembers))
            throw new AppException(StatusCodes.Status409Conflict, "RavenDB voting quorum is unavailable");

        var orderedMembers = votingMembers.Keys.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
        if (!orderedMembers.Contains(localNodeTag, StringComparer.Ordinal))
            throw new AppException(StatusCodes.Status409Conflict, "Local RavenDB node is not a voting member");

        var current = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
        if (current?.Value?.Status is ClusterUpdateStatus.Running or ClusterUpdateStatus.Preparing)
            throw new AppException(StatusCodes.Status409Conflict, "A cluster update is already active");
        if (current?.Value is { } previous)
            await RecordHistoryAsync(previous, cancellationToken);

        var state = new ClusterUpdateState {
            Id = Guid.NewGuid(),
            ImageDigest = request.ImageDigest,
            Nodes = orderedMembers.Select(tag => new ClusterUpdateNodeState
                { NodeTag = tag, Status = ClusterUpdateNodeStatus.Preparing }).ToList(),
            Status = ClusterUpdateStatus.Preparing,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var result = await documentStore.Operations.SendAsync(
            new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, state, current?.Index ?? 0),
            token: cancellationToken);
        if (!result.Successful)
            throw new AppException(StatusCodes.Status409Conflict, "Another cluster update was started concurrently");
        return ClusterUpdateTransitions.ToResponse(state);
    }

    public async Task<ClusterUpdateResponse?> GetAsync(CancellationToken cancellationToken) {
        var result = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
        if (result?.Value is not { } state) return null;
        return ClusterUpdateTransitions.ToResponse(state);
    }

    public async Task<IReadOnlyList<ApplicationUpdateHistoryResponse>> GetHistoryAsync(
        CancellationToken cancellationToken) {
        var history = new List<ApplicationUpdateHistoryResponse>();
        for (var start = 0;; start += 128) {
            var page = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValuesOperation<ApplicationUpdateHistoryResponse>(HistoryPrefix, start, 128),
                token: cancellationToken);
            history.AddRange(page.Values.Select(x => x.Value));
            if (page.Count < 128) break;
        }

        return history.OrderByDescending(x => x.StartedAt).ToArray();
    }

    public async Task ArchiveCompletedAsync(CancellationToken cancellationToken) {
        var current = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
        if (current?.Value is { } state)
            await RecordHistoryAsync(state, cancellationToken);
    }


    private async Task RecordHistoryAsync(ClusterUpdateState state, CancellationToken cancellationToken) {
        if (state.Status is ClusterUpdateStatus.Running or ClusterUpdateStatus.Preparing) return;
        var key = HistoryPrefix + state.Id;
        var existing = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ApplicationUpdateHistoryResponse>(key), token: cancellationToken);
        if (existing?.Value is not null) return;
        var entry = new ApplicationUpdateHistoryResponse {
            Id = state.Id, ImageDigest = state.ImageDigest, Status = state.Status.ToString(),
            StartedAt = state.StartedAt, CompletedAt = state.UpdatedAt
        };
        await documentStore.Operations.SendAsync(
            new PutCompareExchangeValueOperation<ApplicationUpdateHistoryResponse>(key, entry, 0),
            token: cancellationToken);
    }

    public async Task<ClusterUpdateResponse?> PrepareLocalAsync(string localTag, Guid updateId, bool succeeded,
        string? error, CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { } state || state.Id != updateId ||
                state.Status != ClusterUpdateStatus.Preparing)
                return null;
            var node = state.Nodes.FirstOrDefault(x => x.NodeTag == localTag);
            if (node is null) return null;
            var updated = ClusterUpdateTransitions.Prepare(state, localTag, succeeded, error);

            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, updated, current.Index),
                token: cancellationToken);
            if (result.Successful) return ClusterUpdateTransitions.ToResponse(updated);
        }
    }

    public async Task<ClusterUpdateResponse?> ClaimLocalAsync(string localTag, ClusterTopologyResponse topology,
        CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { Status: ClusterUpdateStatus.Running } state ||
                !ClusterUpdateTransitions.IsClaimable(state, localTag))
                return null;

            var members = topology.Topology.Members.Keys.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
            if (!members.SequenceEqual(state.Nodes.Select(node => node.NodeTag), StringComparer.Ordinal))
                throw new InvalidOperationException("RavenDB voting membership changed during cluster update");
            var connected = await GetConnectedMembersAsync(topology, cancellationToken);
            if (!UpdateSafetyPolicy.HasRestartQuorum(members.Length, connected))
                return null;

            if (state.Nodes[state.CurrentNodeIndex].Status == ClusterUpdateNodeStatus.Claimed)
                return ClusterUpdateTransitions.ToResponse(state);
            var claimed = ClusterUpdateTransitions.Claim(state);
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, claimed, current.Index),
                token: cancellationToken);
            if (result.Successful)
                return ClusterUpdateTransitions.ToResponse(claimed);
        }
    }

    public async Task<ClusterUpdateResponse> CompleteLocalAsync(string localTag,
        CompleteClusterUpdateRequest request, CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { } state || state.Id != request.UpdateId ||
                state.CurrentNodeIndex >= state.Nodes.Count ||
                state.Nodes[state.CurrentNodeIndex].NodeTag != localTag ||
                state.Nodes[state.CurrentNodeIndex].Status != ClusterUpdateNodeStatus.Claimed)
                throw new InvalidOperationException("This node does not own the active update step");

            var updated = ClusterUpdateTransitions.Complete(state, request);

            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, updated, current.Index),
                token: cancellationToken);
            if (result.Successful) {
                return ClusterUpdateTransitions.ToResponse(updated);
            }
        }
    }

    public async Task<ClusterUpdateResponse> CancelAsync(Guid id, CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { } state || state.Id != id)
                throw new KeyNotFoundException("Cluster update not found");
            if (state.Status is not (ClusterUpdateStatus.Running or ClusterUpdateStatus.Preparing))
                return ClusterUpdateTransitions.ToResponse(state);
            if (state.Nodes.Any(node => node.Status == ClusterUpdateNodeStatus.Claimed))
                throw new AppException(StatusCodes.Status409Conflict,
                    "Cannot cancel while a node update is in progress");

            var cancelled = ClusterUpdateTransitions.Cancel(state);
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, cancelled, current.Index),
                token: cancellationToken);
            if (result.Successful) {
                return ClusterUpdateTransitions.ToResponse(cancelled);
            }
        }
    }

    private async Task<int> GetConnectedMembersAsync(ClusterTopologyResponse topology,
        CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(topology.Leader)) return 0;
        var leaderTopology = topology;
        if (topology.NodeTag != topology.Leader) {
            if (!topology.Topology.Members.TryGetValue(topology.Leader, out var leaderUrl)) return 0;
            using var leaderStore = new DocumentStore();
            leaderStore.Urls = [leaderUrl];
            leaderStore.Certificate = documentStore.Certificate;
            leaderStore.Conventions = new Raven.Client.Documents.Conventions.DocumentConventions {
                DisableTopologyUpdates = true,
                DisposeCertificate = false,
                CreateHttpClient = documentStore.Conventions.CreateHttpClient,
                HttpClientType = documentStore.Conventions.HttpClientType
            };
            leaderStore.Initialize();
            leaderTopology = await leaderStore.Maintenance.Server
                .SendAsync(new GetClusterTopologyOperation(), cancellationToken);
        }

        if (leaderTopology.NodeTag != leaderTopology.Leader) return 0;
        return topology.Topology.Members.Keys.Count(tag =>
            leaderTopology.Topology.Members.ContainsKey(tag) &&
            (tag == leaderTopology.NodeTag ||
             (leaderTopology.Status.TryGetValue(tag, out var status) && status.Connected)));
    }

    internal static bool HasQuorum(int members, int connected) {
        return members > 0 && connected >= members / 2 + 1;
    }
}