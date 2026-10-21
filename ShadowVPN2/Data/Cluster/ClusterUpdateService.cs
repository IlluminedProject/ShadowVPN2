using System.Text.RegularExpressions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Http;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateService(IDocumentStore documentStore) {
    private const string StateKey = "ShadowVPN2/ClusterUpdate/State";
    private static readonly Regex DigestPattern = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public async Task<ClusterUpdateResponse> StartAsync(StartClusterUpdateRequest request,
        ClusterTopologyResponse topology, int localNodeNumber, CancellationToken cancellationToken) {
        if (!UpdateSafetyPolicy.AllowsRollingUpdate(request.BackwardCompatible)) {
            throw new InvalidOperationException(
                "Rolling update requires a backward-compatible schema change; deploy an expand-and-contract release first");
        }

        if (!DigestPattern.IsMatch(request.ImageDigest))
            throw new ArgumentException("ImageDigest must be a lowercase sha256 digest", nameof(request));

        var votingMembers = topology.Topology.Members;
        var connectedMembers = votingMembers.Keys.Count(tag =>
            topology.Status.TryGetValue(tag, out var status) && status.Connected);
        if (!HasQuorum(votingMembers.Count, connectedMembers))
            throw new InvalidOperationException("RavenDB voting quorum is unavailable");

        var orderedMembers = votingMembers.Keys.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
        if (!orderedMembers.Contains(ClusterService.GetNodeTag(localNodeNumber), StringComparer.Ordinal))
            throw new InvalidOperationException("Local RavenDB node is not a voting member");

        var current = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
        if (current?.Value?.Status == ClusterUpdateStatus.Running)
            throw new InvalidOperationException("A cluster update is already active");

        var state = new ClusterUpdateState {
            Id = Guid.NewGuid(),
            ImageDigest = request.ImageDigest,
            Nodes = orderedMembers.Select(tag => new ClusterUpdateNodeState { NodeTag = tag }).ToList(),
            Status = ClusterUpdateStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var result = await documentStore.Operations.SendAsync(
            new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, state, current?.Index ?? 0),
            token: cancellationToken);
        if (!result.Successful)
            throw new InvalidOperationException("Another cluster update was started concurrently");
        return Map(state);
    }

    public async Task<ClusterUpdateResponse?> GetAsync(CancellationToken cancellationToken) {
        var result = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
        return result?.Value is { } state ? Map(state) : null;
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
            var connected = members.Count(tag => topology.Status.TryGetValue(tag, out var status) && status.Connected);
            if (!UpdateSafetyPolicy.HasRestartQuorum(members.Length, connected))
                return null;

            if (state.Nodes[state.CurrentNodeIndex].Status == ClusterUpdateNodeStatus.Claimed)
                return Map(state);
            var claimed = ClusterUpdateTransitions.Claim(state);
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, claimed, current.Index),
                token: cancellationToken);
            if (result.Successful)
                return Map(claimed);
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
            if (result.Successful)
                return Map(updated);
        }
    }

    public async Task<ClusterUpdateResponse> CancelAsync(Guid id, CancellationToken cancellationToken) {
        while (true) {
            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<ClusterUpdateState>(StateKey), token: cancellationToken);
            if (current?.Value is not { } state || state.Id != id)
                throw new KeyNotFoundException("Cluster update not found");
            if (state.Status != ClusterUpdateStatus.Running)
                return Map(state);
            if (state.Nodes.Any(node => node.Status == ClusterUpdateNodeStatus.Claimed))
                throw new InvalidOperationException("Cannot cancel while a node update is in progress");

            var cancelled = Copy(state);
            cancelled.Status = ClusterUpdateStatus.Cancelled;
            cancelled.UpdatedAt = DateTimeOffset.UtcNow;
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<ClusterUpdateState>(StateKey, cancelled, current.Index),
                token: cancellationToken);
            if (result.Successful)
                return Map(cancelled);
        }
    }

    internal static bool HasQuorum(int members, int connected) {
        return members > 0 && connected >= members / 2 + 1;
    }

    private static ClusterUpdateState Copy(ClusterUpdateState state) {
        return new ClusterUpdateState {
            Id = state.Id,
            ImageDigest = state.ImageDigest,
            Nodes = state.Nodes.Select(node => new ClusterUpdateNodeState {
                NodeTag = node.NodeTag,
                Status = node.Status,
                Error = node.Error,
                ClaimedAt = node.ClaimedAt,
                CompletedAt = node.CompletedAt
            }).ToList(),
            CurrentNodeIndex = state.CurrentNodeIndex,
            Status = state.Status,
            Error = state.Error,
            StartedAt = state.StartedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static ClusterUpdateResponse Map(ClusterUpdateState state) {
        return new ClusterUpdateResponse {
            Id = state.Id,
            ImageDigest = state.ImageDigest,
            Status = state.Status.ToString(),
            Nodes = state.Nodes.Select(node => new ClusterUpdateNodeResponse {
                NodeTag = node.NodeTag,
                Status = node.Status.ToString(),
                Error = node.Error
            }).ToArray(),
            CurrentNodeTag = state.CurrentNodeIndex < state.Nodes.Count
                ? state.Nodes[state.CurrentNodeIndex].NodeTag
                : null,
            Error = state.Error
        };
    }
}