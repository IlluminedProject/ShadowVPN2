using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Cluster;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ClusterUpdateTransitionsTests {
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Reconciliation_failures_survive_transitions_and_fail_only_at_limit() {
        var state = CreateState("A", "B", "C");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Completed;
        state.CurrentNodeIndex = 1;
        var first = ClusterUpdateTransitions.RecordReconciliationFailure(state, "B", "TLS rejected", 2);
        first.Status.Should().Be(ClusterUpdateStatus.Running);
        first.Nodes[1].ReconciliationFailures.Should().Be(1);
        var claimed = ClusterUpdateTransitions.Claim(first);
        var failed = ClusterUpdateTransitions.RecordReconciliationFailure(claimed, "B", "TLS rejected", 2);
        failed.Status.Should().Be(ClusterUpdateStatus.Failed);
        failed.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Completed);
        failed.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Failed);
        failed.Nodes[2].Status.Should().Be(ClusterUpdateNodeStatus.Skipped);
        failed.Error.Should().Contain("2 attempts").And.Contain("TLS rejected");
        state.Nodes[1].ReconciliationFailures.Should().Be(0);
    }

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
    public void New_rollout_cannot_claim_a_node_before_all_nodes_are_prepared() {
        var state = CreateState("NAA", "NAB");
        state.Status = ClusterUpdateStatus.Running;
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Prepared;
        state.Nodes[1].Status = ClusterUpdateNodeStatus.Preparing;

        ClusterUpdateTransitions.IsClaimable(state, "NAA").Should().BeFalse();
        ClusterUpdateTransitions.IsClaimable(state, "NAB").Should().BeFalse();
    }

    [Fact]
    public void Rollout_can_claim_prepared_node_after_global_prepare_barrier() {
        var state = CreateState("NAA", "NAB");
        state.Status = ClusterUpdateStatus.Running;
        state.Nodes.ForEach(node => node.Status = ClusterUpdateNodeStatus.Prepared);

        ClusterUpdateTransitions.IsClaimable(state, "NAA").Should().BeTrue();
        ClusterUpdateTransitions.IsClaimable(state, "NAB").Should().BeFalse();
    }

    [Fact]
    public void Legacy_rollout_can_resume_after_claim_and_completion() {
        var state = CreateState("NAA", "NAB");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Completed;
        state.CurrentNodeIndex = 1;

        ClusterUpdateTransitions.IsClaimable(state, "NAB").Should().BeTrue();
    }

    [Fact]
    public void Legacy_rollout_can_retry_current_claim() {
        var state = CreateState("NAA", "NAB");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Claimed;

        ClusterUpdateTransitions.IsClaimable(state, "NAA").Should().BeTrue();
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
        updated.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Skipped);
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

    [Fact]
    public void Preparation_failure_finishes_other_nodes_and_preserves_the_error() {
        var state = CreateState("NAA", "NAB", "NAC");
        state.Status = ClusterUpdateStatus.Preparing;
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Prepared;
        state.Nodes[1].Status = ClusterUpdateNodeStatus.Preparing;
        state.Nodes[2].Status = ClusterUpdateNodeStatus.Preparing;

        var updated = ClusterUpdateTransitions.Prepare(state, "NAB", false, "Docker image pull failed");

        updated.Status.Should().Be(ClusterUpdateStatus.Failed);
        updated.Error.Should().Be("Docker image pull failed");
        updated.Nodes.Select(node => node.Status).Should().Equal(
            ClusterUpdateNodeStatus.Skipped, ClusterUpdateNodeStatus.Failed, ClusterUpdateNodeStatus.Skipped);
        updated.Nodes[1].Error.Should().Be(updated.Error);
        updated.Nodes.Should().OnlyContain(node => node.CompletedAt == updated.UpdatedAt);
        state.Status.Should().Be(ClusterUpdateStatus.Preparing);
        state.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Prepared);
        state.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Preparing);
        ClusterUpdateTransitions.ToResponse(updated).CurrentNodeTag.Should().BeNull();
    }

    [Fact]
    public void Preparation_success_waits_for_all_nodes_before_starting_rollout() {
        var state = CreateState("NAA", "NAB");
        state.Status = ClusterUpdateStatus.Preparing;
        state.Nodes.ForEach(node => node.Status = ClusterUpdateNodeStatus.Preparing);

        var first = ClusterUpdateTransitions.Prepare(state, "NAA", true, null);
        var ready = ClusterUpdateTransitions.Prepare(first, "NAB", true, null);

        first.Status.Should().Be(ClusterUpdateStatus.Preparing);
        first.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Preparing);
        ready.Status.Should().Be(ClusterUpdateStatus.Running);
        ready.Nodes.Should().OnlyContain(node => node.Status == ClusterUpdateNodeStatus.Prepared);
        ClusterUpdateTransitions.IsClaimable(ready, "NAA").Should().BeTrue();
    }

    [Theory]
    [InlineData(ClusterUpdateNodeStatus.Pending)]
    [InlineData(ClusterUpdateNodeStatus.Prepared)]
    public void Rolling_failure_preserves_completed_nodes_and_skips_remaining_nodes(ClusterUpdateNodeStatus waiting) {
        var state = CreateState("NAA", "NAB", "NAC");
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Completed;
        state.Nodes[0].CompletedAt = state.StartedAt;
        state.CurrentNodeIndex = 1;
        state.Nodes[1].Status = ClusterUpdateNodeStatus.Claimed;
        state.Nodes[2].Status = waiting;

        var updated = ClusterUpdateTransitions.Complete(state, new CompleteClusterUpdateRequest {
            UpdateId = state.Id, Succeeded = false, ImageDigest = Digest, Error = "Container health check failed"
        });

        updated.Status.Should().Be(ClusterUpdateStatus.Failed);
        updated.Nodes.Select(node => node.Status).Should().Equal(
            ClusterUpdateNodeStatus.Completed, ClusterUpdateNodeStatus.Failed, ClusterUpdateNodeStatus.Skipped);
        updated.Nodes[0].CompletedAt.Should().Be(state.StartedAt);
        updated.Nodes[1].Error.Should().Be("Container health check failed");
        updated.Nodes[2].CompletedAt.Should().Be(updated.UpdatedAt);
        state.Nodes[2].Status.Should().Be(waiting);
        ClusterUpdateTransitions.IsClaimable(updated, "NAC").Should().BeFalse();
    }

    [Theory]
    [InlineData(ClusterUpdateStatus.Preparing, ClusterUpdateNodeStatus.Preparing)]
    [InlineData(ClusterUpdateStatus.Preparing, ClusterUpdateNodeStatus.Prepared)]
    [InlineData(ClusterUpdateStatus.Running, ClusterUpdateNodeStatus.Pending)]
    [InlineData(ClusterUpdateStatus.Running, ClusterUpdateNodeStatus.Prepared)]
    public void Cancellation_finishes_unstarted_nodes_and_preserves_completed_outcomes(
        ClusterUpdateStatus operationStatus, ClusterUpdateNodeStatus waiting) {
        var state = CreateState("NAA", "NAB", "NAC");
        state.Status = operationStatus;
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Completed;
        state.Nodes[0].CompletedAt = state.StartedAt;
        state.Nodes[1].Status = waiting;
        state.Nodes[2].Status = ClusterUpdateNodeStatus.Failed;
        state.Nodes[2].Error = "Original failure";

        var updated = ClusterUpdateTransitions.Cancel(state);

        updated.Status.Should().Be(ClusterUpdateStatus.Cancelled);
        updated.Nodes.Select(node => node.Status).Should().Equal(
            ClusterUpdateNodeStatus.Completed, ClusterUpdateNodeStatus.Cancelled, ClusterUpdateNodeStatus.Failed);
        updated.Nodes[0].CompletedAt.Should().Be(state.StartedAt);
        updated.Nodes[1].CompletedAt.Should().Be(updated.UpdatedAt);
        updated.Nodes[2].Error.Should().Be("Original failure");
        state.Nodes[1].Status.Should().Be(waiting);
        ClusterUpdateTransitions.ToResponse(updated).CurrentNodeTag.Should().BeNull();
    }

    [Theory]
    [InlineData(ClusterUpdateStatus.Failed, "Skipped")]
    [InlineData(ClusterUpdateStatus.Cancelled, "Cancelled")]
    public void Legacy_terminal_responses_normalize_unfinished_nodes_without_mutating_persisted_state(
        ClusterUpdateStatus operationStatus, string expectedStatus) {
        var state = CreateState("NAA", "NAB", "NAC", "NAD", "NAE", "NAF");
        state.Status = operationStatus;
        state.Error = "Original operation error";
        state.Nodes[0].Status = ClusterUpdateNodeStatus.Preparing;
        state.Nodes[1].Status = ClusterUpdateNodeStatus.Prepared;
        state.Nodes[2].Status = ClusterUpdateNodeStatus.Pending;
        state.Nodes[3].Status = ClusterUpdateNodeStatus.Claimed;
        state.Nodes[4].Status = ClusterUpdateNodeStatus.Failed;
        state.Nodes[4].Error = "Original node error";
        state.Nodes[5].Status = ClusterUpdateNodeStatus.Completed;

        var response = ClusterUpdateTransitions.ToResponse(state);

        response.Nodes.Select(node => node.Status).Should().Equal(
            expectedStatus, expectedStatus, expectedStatus, expectedStatus, "Failed", "Completed");
        response.Nodes[4].Error.Should().Be("Original node error");
        response.Error.Should().Be("Original operation error");
        response.CurrentNodeTag.Should().BeNull();
        state.Nodes[0].Status.Should().Be(ClusterUpdateNodeStatus.Preparing);
        state.Nodes[1].Status.Should().Be(ClusterUpdateNodeStatus.Prepared);
        state.Nodes[2].Status.Should().Be(ClusterUpdateNodeStatus.Pending);
        state.Nodes[3].Status.Should().Be(ClusterUpdateNodeStatus.Claimed);
    }

    [Theory]
    [InlineData(ClusterUpdateStatus.Preparing, ClusterUpdateNodeStatus.Preparing)]
    [InlineData(ClusterUpdateStatus.Running, ClusterUpdateNodeStatus.Prepared)]
    public void Active_responses_preserve_waiting_nodes_and_current_node(
        ClusterUpdateStatus operationStatus, ClusterUpdateNodeStatus nodeStatus) {
        var state = CreateState("NAA");
        state.Status = operationStatus;
        state.Nodes[0].Status = nodeStatus;

        var response = ClusterUpdateTransitions.ToResponse(state);

        response.Nodes[0].Status.Should().Be(nodeStatus.ToString());
        response.CurrentNodeTag.Should().Be("NAA");
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