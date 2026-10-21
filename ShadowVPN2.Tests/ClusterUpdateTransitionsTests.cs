using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Cluster;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ClusterUpdateTransitionsTests {
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Claim_is_idempotent_for_current_node_and_rejects_other_nodes() {
        var state = CreateState("NAA", "NAB");

        ClusterUpdateTransitions.IsClaimable(state, "NAB").Should().BeFalse();
        ClusterUpdateTransitions.IsClaimable(state, "NAA").Should().BeTrue();
        var claimed = ClusterUpdateTransitions.Claim(state);
        var retried = ClusterUpdateTransitions.Claim(claimed);
        claimed.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Claimed);
        retried.Nodes[0].ClaimedAt.Should().Be(claimed.Nodes[0].ClaimedAt);
        retried.CurrentNodeIndex.Should().Be(0);
    }

    [Fact]
    public void Complete_advances_only_when_new_image_digest_matches_target() {
        var state = CreateState("NAA", "NAB");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Claimed;

        var updated = ClusterUpdateTransitions.Complete(state, new CompleteClusterUpdateRequest {
            UpdateId = state.Id,
            Succeeded = true,
            ImageDigest = Digest
        });

        updated.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Completed);
        updated.CurrentNodeIndex.Should().Be(1);
        updated.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Pending);
        updated.Status.Should().Be(ClusterUpdateStatus.Running);
    }

    [Fact]
    public void Complete_rejects_success_report_for_wrong_digest_and_stops_rollout() {
        var state = CreateState("NAA", "NAB");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Claimed;

        var updated = ClusterUpdateTransitions.Complete(state, new CompleteClusterUpdateRequest {
            UpdateId = state.Id,
            Succeeded = true,
            ImageDigest = "sha256:" + new string('b', 64)
        });

        updated.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Failed);
        updated.Status.Should().Be(ClusterUpdateStatus.Failed);
        updated.CurrentNodeIndex.Should().Be(0);
        updated.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Pending);
    }

    [Fact]
    public void Complete_marks_rollout_complete_after_last_node() {
        var state = CreateState("NAA");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Claimed;

        var updated = ClusterUpdateTransitions.Complete(state, new CompleteClusterUpdateRequest {
            UpdateId = state.Id,
            Succeeded = true,
            ImageDigest = Digest
        });

        updated.Status.Should().Be(ClusterUpdateStatus.Completed);
        updated.CurrentNodeIndex.Should().Be(1);
    }

    private static ClusterUpdateState CreateState(params string[] tags) {
        return new ClusterUpdateState {
            Id = Guid.NewGuid(),
            ImageDigest = Digest,
            Nodes = tags.Select(tag => new ClusterUpdateNodeState { NodeTag = tag }).ToList(),
            Status = ClusterUpdateStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}