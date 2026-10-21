using AwesomeAssertions;
using ShadowVPN2.Data.Cluster;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class UpdateSafetyPolicyTests {
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 1, false)]
    [InlineData(5, 3, true)]
    [InlineData(5, 2, false)]
    public void HasVotingQuorum_requires_a_strict_majority(int votingMembers, int connectedMembers,
        bool expected) {
        UpdateSafetyPolicy.HasVotingQuorum(votingMembers, connectedMembers).Should().Be(expected);
    }


    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(5, 3, false)]
    [InlineData(5, 4, true)]
    public void HasRestartQuorum_preserves_voting_majority_during_container_restart(int votingMembers,
        int connectedVotingMembers, bool expected) {
        UpdateSafetyPolicy.HasRestartQuorum(votingMembers, connectedVotingMembers).Should().Be(expected);
    }

    [Theory]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("", false)]
    [InlineData("latest", false)]
    [InlineData("sha256:abc", false)]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ", false)]
    public void IsValidImageDigest_accepts_only_canonical_sha256_digests(string digest, bool expected) {
        UpdateSafetyPolicy.IsValidImageDigest(digest).Should().Be(expected);
    }

    [Fact]
    public void HasVotingQuorum_rejects_empty_and_negative_topologies() {
        UpdateSafetyPolicy.HasVotingQuorum(0, 0).Should().BeFalse();
        UpdateSafetyPolicy.HasVotingQuorum(-1, -1).Should().BeFalse();
        UpdateSafetyPolicy.HasVotingQuorum(3, -1).Should().BeFalse();
    }
}