namespace ShadowVPN2.Contracts.Auth;

public sealed class UpdateAuthSettingsRequest {
    public bool EnableLocalLogin { get; set; }
    public bool SelfRegistrationEnabled { get; set; } = true;
    public bool EnableOidc { get; set; }
    public OidcAuthSettings? OidcSettings { get; set; }
}