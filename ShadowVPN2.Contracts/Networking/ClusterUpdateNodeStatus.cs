using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClusterUpdateNodeStatus {
    Pending,
    Claimed,
    Completed,
    Failed,
    Preparing,
    Prepared,
    Skipped,
    Cancelled,
    Aborted
}