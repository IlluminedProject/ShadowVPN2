using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public static class ClusterUpdateTransitions {
    public static bool IsClaimable(ClusterUpdateState state, string localNodeTag) {
        return state.Status == ClusterUpdateStatus.Running &&
               (state.Nodes.All(node => node.Status is ClusterUpdateNodeStatus.Pending
                    or ClusterUpdateNodeStatus.Claimed or ClusterUpdateNodeStatus.Completed) ||
                state.Nodes.All(node => node.Status is ClusterUpdateNodeStatus.Prepared
                    or ClusterUpdateNodeStatus.Claimed or ClusterUpdateNodeStatus.Completed)) &&
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
        else if (updated.Nodes.All(node => node.Status == ClusterUpdateNodeStatus.Prepared)) {
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

    public static ClusterUpdateResponse ToResponse(ClusterUpdateState state) {
        return new ClusterUpdateResponse {
            Id = state.Id,
            ImageDigest = state.ImageDigest,
            Status = state.Status.ToString(),
            Nodes = state.Nodes.Select(node => new ClusterUpdateNodeResponse {
                NodeTag = node.NodeTag,
                Status = GetTerminalNodeStatus(state.Status, node.Status).ToString(),
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
}