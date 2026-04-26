namespace ShadowVPN2.Data;

public class AuthSettingsResponse
{
    public bool EnableLocalLogin { get; set; }
    public bool EnableOidc { get; set; }
    public OidcAuthSettings? OidcSettings { get; set; }
}
