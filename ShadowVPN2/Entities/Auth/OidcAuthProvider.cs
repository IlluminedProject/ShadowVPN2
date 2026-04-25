using System.Threading.Tasks;

namespace ShadowVPN2.Entities.Auth;

public class OidcAuthProvider : AuthProvider
{
    public string SchemeName { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public override Task RegisterSchemeAsync(Infrastructure.Authentication.DynamicAuthenticationManager manager)
    {
        return manager.AddOrUpdateOidcProviderAsync(this);
    }
}
