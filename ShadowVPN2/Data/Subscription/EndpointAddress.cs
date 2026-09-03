namespace ShadowVPN2.Data.Subscription;

internal static class EndpointAddress {
    public static string GetHost(string address) {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return uri.Host;

        if (address.Contains(':') && Uri.TryCreate($"https://{address}", UriKind.Absolute, out uri))
            return uri.Host;

        return address;
    }

    public static string FormatHostForUri(string host) {
        return host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
    }
}