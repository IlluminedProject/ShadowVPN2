using System;
using System.Threading.Tasks;

namespace ShadowVPN2.Entities.Auth;

public abstract class AuthProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public abstract Task RegisterSchemeAsync(Infrastructure.Authentication.DynamicAuthenticationManager manager);
}
