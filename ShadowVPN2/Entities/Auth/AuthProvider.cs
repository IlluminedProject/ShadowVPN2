using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Entities.Auth;

public abstract class AuthProvider {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public abstract Task RegisterSchemeAsync(DynamicAuthenticationManager manager);
}