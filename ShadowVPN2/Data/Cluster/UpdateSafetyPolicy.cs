using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public static class UpdateSafetyPolicy {
    public static bool IsValidImageDigest(string? digest) {
        return ImageDigest.IsValid(digest);
    }

    public static bool HasVotingQuorum(int votingMembers, int connectedVotingMembers) {
        return votingMembers > 0 && connectedVotingMembers >= votingMembers / 2 + 1;
    }

    public static bool HasRestartQuorum(int votingMembers, int connectedVotingMembers) {
        if (!HasVotingQuorum(votingMembers, connectedVotingMembers))
            return false;
        if (votingMembers == 1)
            return true;
        return connectedVotingMembers - 1 >= votingMembers / 2 + 1;
    }

    public static bool AllowsRollingUpdate(bool backwardCompatibleSchema) {
        return backwardCompatibleSchema;
    }
}