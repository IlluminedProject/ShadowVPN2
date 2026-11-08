namespace ShadowVPN2.Contracts.Updater;

public sealed class UpdaterRequest {
    public required UpdaterOperation Operation { get; init; }
    public Guid UpdateId { get; init; }
    public string ImageDigest { get; init; } = string.Empty;
}
