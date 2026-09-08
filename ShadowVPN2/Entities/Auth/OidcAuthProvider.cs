using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Entities.Auth;

public class OidcAuthProvider : AuthProvider {
    public string SchemeName { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid email profile";
    public bool DeviceFlowEnabled { get; set; }

    public override Task RegisterSchemeAsync(DynamicAuthenticationManager manager) {
        return manager.AddOrUpdateOidcProviderAsync(this);
    }
}