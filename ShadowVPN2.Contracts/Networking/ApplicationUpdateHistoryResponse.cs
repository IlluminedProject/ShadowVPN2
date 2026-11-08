using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;
public sealed class ApplicationUpdateHistoryResponse {
    public Guid Id { get; set; }
    public string ImageDigest { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClusterUpdateStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}