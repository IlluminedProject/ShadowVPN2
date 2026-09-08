using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Entities.Auth;

public class LocalAuthProvider : AuthProvider {
    public LocalAuthProvider() {
        DisplayName = "Local Database";
    }

    public override Task RegisterSchemeAsync(DynamicAuthenticationManager manager) {
        return Task.CompletedTask;
    }
}