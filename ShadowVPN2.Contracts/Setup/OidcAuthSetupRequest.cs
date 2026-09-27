namespace ShadowVPN2.Contracts.Setup;

public sealed class OidcAuthSetupRequest {
    public string SchemeName { get; set; } = "OIDC";
    public string DisplayName { get; set; } = "Corporate Login";
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}