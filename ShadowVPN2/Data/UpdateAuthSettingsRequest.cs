namespace ShadowVPN2.Data;

public class UpdateAuthSettingsRequest
{
    public bool EnableLocalLogin { get; set; }
    public bool SelfRegistrationEnabled { get; set; } = true;
    public bool EnableOidc { get; set; }
    public OidcAuthSettings? OidcSettings { get; set; }
}

public class OidcAuthSettings
{
    public string DisplayName { get; set; } = "Login with OAuth";
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
