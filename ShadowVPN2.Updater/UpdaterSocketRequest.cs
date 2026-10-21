namespace ShadowVPN2.Updater;

internal sealed record UpdaterSocketRequest {
    public string Operation { get; init; } = "";
    public Guid UpdateId { get; init; }
    public string ImageDigest { get; init; } = "";
}