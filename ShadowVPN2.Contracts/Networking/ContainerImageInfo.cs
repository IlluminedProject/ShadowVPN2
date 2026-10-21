namespace ShadowVPN2.Contracts.Networking;

public sealed class ContainerImageInfo {
    public string? ImageRepository { get; set; }
    public string[] RepoDigests { get; set; } = [];
    public string? Os { get; set; }
    public string? Architecture { get; set; }
    public string? Variant { get; set; }
}