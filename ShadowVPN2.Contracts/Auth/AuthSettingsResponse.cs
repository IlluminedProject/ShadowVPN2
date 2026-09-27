namespace ShadowVPN2.Contracts.Auth;

public sealed class AuthSettingsResponse {
    public bool EnableLocalLogin { get; set; }
    public bool SelfRegistrationEnabled { get; set; }
    public bool EnableOidc { get; set; }
    public OidcAuthSettings? OidcSettings { get; set; }
}