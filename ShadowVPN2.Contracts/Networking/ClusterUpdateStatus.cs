using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClusterUpdateStatus {
    Running = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
    Preparing = 4
}
