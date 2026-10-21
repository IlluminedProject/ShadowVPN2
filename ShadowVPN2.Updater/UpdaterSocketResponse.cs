namespace ShadowVPN2.Updater;

internal sealed record UpdaterSocketResponse {
    public Guid UpdateId { get; init; }
    public string ImageDigest { get; init; } = "";
    public string Status { get; init; } = "Idle";
    public string? Error { get; init; }
}