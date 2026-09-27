namespace ShadowVPN2.Contracts.Setup;

public sealed class NodeSetupRequest {
    public string NodeName { get; set; } = string.Empty;
    public string? Domain { get; set; }
}