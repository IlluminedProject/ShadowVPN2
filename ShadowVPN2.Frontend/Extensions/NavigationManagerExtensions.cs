using Microsoft.AspNetCore.Components;

namespace ShadowVPN2.Frontend.Extensions;

public static class NavigationManagerExtensions {
    public static int GetCurrentWebPort(this NavigationManager navigation) {
        var uri = new Uri(navigation.Uri);
        return uri.IsDefaultPort
            ? uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;
    }
}