using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;
public sealed class ClusterUpdateResponse {
    public required Guid Id { get; set; }
    public required string ImageDigest { get; set; }
    public string? DesiredImageDigest { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ClusterUpdateStatus Status { get; set; }
    public required IReadOnlyList<ClusterUpdateNodeResponse> Nodes { get; set; }
    public string? CurrentNodeTag { get; set; }
    public string? Error { get; set; }
}