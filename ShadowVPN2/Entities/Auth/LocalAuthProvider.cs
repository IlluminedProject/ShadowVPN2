using System.Threading.Tasks;

namespace ShadowVPN2.Entities.Auth;

public class LocalAuthProvider : AuthProvider
{
    public LocalAuthProvider()
    {
        DisplayName = "Local Database";
    }

    public override Task RegisterSchemeAsync(Infrastructure.Authentication.DynamicAuthenticationManager manager)
    {
        return Task.CompletedTask;
    }
}
