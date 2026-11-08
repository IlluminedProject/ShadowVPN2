using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public static class ClusterUpdateTransitions {
    public static bool IsClaimable(ClusterUpdateState state, string localNodeTag) {
        return state.Status == ClusterUpdateStatus.Running &&
               state.Nodes.All(node => node.Status is ClusterUpdateNodeStatus.Pending
                   or ClusterUpdateNodeStatus.Prepared or ClusterUpdateNodeStatus.Claimed
                   or ClusterUpdateNodeStatus.Completed or ClusterUpdateNodeStatus.Skipped) &&
               state.CurrentNodeIndex < state.Nodes.Count &&
               state.Nodes[state.CurrentNodeIndex].NodeTag == localNodeTag &&
               state.Nodes[state.CurrentNodeIndex].Status is ClusterUpdateNodeStatus.Pending
                   or ClusterUpdateNodeStatus.Prepared or ClusterUpdateNodeStatus.Claimed;
    }

    public static ClusterUpdateState Claim(ClusterUpdateState state) {
        var claimed = Copy(state);
        var node = claimed.Nodes[claimed.CurrentNodeIndex];
        if (node.Status is ClusterUpdateNodeStatus.Pending or ClusterUpdateNodeStatus.Prepared) {
            node.Status = ClusterUpdateNodeStatus.Claimed;
            node.ClaimedAt = DateTimeOffset.UtcNow;
            claimed.UpdatedAt = node.ClaimedAt.Value;
        }

        return claimed;
    }

    public static ClusterUpdateState Prepare(ClusterUpdateState state, string localTag, bool succeeded, string? error) {
        var updated = Copy(state);
        var node = updated.Nodes.First(node => node.NodeTag == localTag);
        node.Status = succeeded ? ClusterUpdateNodeStatus.Prepared : ClusterUpdateNodeStatus.Failed;
        node.Error = succeeded ? null : error ?? "Image preparation failed";
        updated.UpdatedAt = DateTimeOffset.UtcNow;
        if (!succeeded) {
            node.CompletedAt = updated.UpdatedAt;
            updated.Status = ClusterUpdateStatus.Failed;
            updated.Error = node.Error;
            FinishRemainingNodes(updated);
        }
        else if (updated.Nodes.All(node => node.Status is ClusterUpdateNodeStatus.Prepared
                     or ClusterUpdateNodeStatus.Skipped)) {
            updated.Status = ClusterUpdateStatus.Running;
        }

        return updated;
    }

    public static ClusterUpdateState TimeoutPreparing(ClusterUpdateState state, DateTimeOffset now,
        TimeSpan timeout) {
        var updated = Copy(state);
        var changed = false;
        foreach (var node in updated.Nodes.Where(node => node.Status == ClusterUpdateNodeStatus.Preparing &&
                                                         (node.PreparingAt ?? state.StartedAt) is { } started &&
                                                         now - started >= timeout)) {
            node.Status = ClusterUpdateNodeStatus.Skipped;
            node.Error = "Node did not become available during preparation timeout";
            node.CompletedAt = now;
            changed = true;
        }

        if (changed) {
            updated.UpdatedAt = now;
            while (updated.CurrentNodeIndex < updated.Nodes.Count &&
                   updated.Nodes[updated.CurrentNodeIndex].Status == ClusterUpdateNodeStatus.Skipped)
                updated.CurrentNodeIndex++;
            if (updated.CurrentNodeIndex == updated.Nodes.Count)
                updated.Status = ClusterUpdateStatus.Completed;
            else if (updated.Nodes.All(node => node.Status is ClusterUpdateNodeStatus.Prepared
                         or ClusterUpdateNodeStatus.Skipped))
                updated.Status = ClusterUpdateStatus.Running;
        }

        return updated;
    }

    public static ClusterUpdateState Cancel(ClusterUpdateState state) {
        var cancelled = Copy(state);
        cancelled.Status = ClusterUpdateStatus.Cancelled;
        cancelled.UpdatedAt = DateTimeOffset.UtcNow;
        FinishRemainingNodes(cancelled);
        return cancelled;
    }

    public static ClusterUpdateState CompleteAlreadyUpdated(ClusterUpdateState state, string localTag) {
        var completed = Copy(state);
        if (completed.Status != ClusterUpdateStatus.Running ||
            completed.CurrentNodeIndex >= completed.Nodes.Count ||
            completed.Nodes[completed.CurrentNodeIndex].NodeTag != localTag)
            throw new InvalidOperationException("This node does not own the active update step");

        var node = completed.Nodes[completed.CurrentNodeIndex];
        node.Status = ClusterUpdateNodeStatus.Completed;
        node.Error = null;
        node.CompletedAt = DateTimeOffset.UtcNow;
        completed.CurrentNodeIndex++;
        completed.UpdatedAt = node.CompletedAt.Value;
        if (completed.CurrentNodeIndex == completed.Nodes.Count)
            completed.Status = ClusterUpdateStatus.Completed;
        return completed;
    }

    public static ClusterUpdateState Complete(ClusterUpdateState state, CompleteClusterUpdateRequest request) {
        var completed = Copy(state);
        var node = completed.Nodes[completed.CurrentNodeIndex];
        var digestMatches = string.Equals(request.ImageDigest, state.ImageDigest, StringComparison.Ordinal);
        var succeeded = request.Succeeded && digestMatches;
        node.Status = succeeded ? ClusterUpdateNodeStatus.Completed : ClusterUpdateNodeStatus.Failed;
        node.Error = succeeded ? null : request.Error ?? "Reported image digest does not match target digest";
        node.CompletedAt = DateTimeOffset.UtcNow;
        completed.UpdatedAt = node.CompletedAt.Value;
        if (succeeded) {
            completed.CurrentNodeIndex++;
            if (completed.CurrentNodeIndex == completed.Nodes.Count)
                completed.Status = ClusterUpdateStatus.Completed;
        }
        else {
            completed.Status = ClusterUpdateStatus.Failed;
            completed.Error = node.Error;
            FinishRemainingNodes(completed);
        }

        return completed;
    }

    public static ClusterUpdateState RecordReconciliationFailure(ClusterUpdateState state, string localTag,
        string error, int maxAttempts) {
        var updated = Copy(state);
        var node = updated.Nodes.First(node => node.NodeTag == localTag);
        node.ReconciliationFailures++;
        updated.UpdatedAt = DateTimeOffset.UtcNow;
        if (node.ReconciliationFailures >= maxAttempts) {
            node.Status = ClusterUpdateNodeStatus.Failed;
            node.Error = $"Reconciliation failed after {node.ReconciliationFailures} attempts: {error}";
            node.CompletedAt = updated.UpdatedAt;
            updated.Status = ClusterUpdateStatus.Failed;
            updated.Error = $"Node {localTag}: {node.Error}";
            FinishRemainingNodes(updated);
        }

        return updated;
    }

    public static ClusterUpdateResponse ToResponse(ClusterUpdateState state) {
        return new ClusterUpdateResponse {
            Id = state.Id,
            ImageDigest = state.ImageDigest,
            DesiredImageDigest = state.DesiredImageDigest ?? state.ImageDigest,
            Status = state.Status,
            Nodes = state.Nodes.Select(node => new ClusterUpdateNodeResponse {
                NodeTag = node.NodeTag,
                Status = Enum.Parse<Contracts.Networking.ClusterUpdateNodeStatus>(
                    GetTerminalNodeStatus(state.Status, node.Status).ToString()),
                Error = node.Error
            }).ToArray(),
            CurrentNodeTag = state.Status is ClusterUpdateStatus.Preparing or ClusterUpdateStatus.Running &&
                             state.CurrentNodeIndex < state.Nodes.Count
                ? state.Nodes[state.CurrentNodeIndex].NodeTag
                : null,
            Error = state.Error
        };
    }

    private static void FinishRemainingNodes(ClusterUpdateState state) {
        foreach (var node in state.Nodes) {
            var status = GetTerminalNodeStatus(state.Status, node.Status);
            if (status == node.Status) continue;
            node.Status = status;
            node.CompletedAt = state.UpdatedAt;
        }
    }

    private static ClusterUpdateNodeStatus GetTerminalNodeStatus(ClusterUpdateStatus operationStatus,
        ClusterUpdateNodeStatus nodeStatus) {
        if (nodeStatus is not (ClusterUpdateNodeStatus.Pending or ClusterUpdateNodeStatus.Preparing
            or ClusterUpdateNodeStatus.Prepared or ClusterUpdateNodeStatus.Claimed))
            return nodeStatus;

        return operationStatus switch {
            ClusterUpdateStatus.Failed => ClusterUpdateNodeStatus.Skipped,
            ClusterUpdateStatus.Cancelled => ClusterUpdateNodeStatus.Cancelled,
            _ => nodeStatus
        };
    }

    private static ClusterUpdateState Copy(ClusterUpdateState state) {
        return new ClusterUpdateState {
            Id = state.Id,
            ImageDigest = state.ImageDigest,
            DesiredImageDigest = state.DesiredImageDigest,
            Nodes = state.Nodes.Select(node => new ClusterUpdateNodeState {
                NodeTag = node.NodeTag,
                Status = node.Status,
                Error = node.Error,
                ReconciliationFailures = node.ReconciliationFailures,
                PreparingAt = node.PreparingAt,
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
}