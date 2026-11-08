using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Updater;

public sealed record UpdaterResponse {
    public Guid UpdateId { get; init; }
    public string ImageDigest { get; init; } = string.Empty;
    public UpdaterStatus Status { get; init; }
    public string? Error { get; init; }
    public ContainerImageInfo? ImageInfo { get; init; }
}
