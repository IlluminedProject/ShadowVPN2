using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal readonly record struct RegistryAuthenticationChallenge(Uri Realm, string? Service) {
    public static RegistryAuthenticationChallenge Parse(AuthenticationHeaderValue? challenge) {
        if (challenge?.Parameter is null)
            throw new InvalidOperationException("The image registry requires authentication that is not available");
        string? realm = null;
        string? service = null;
        foreach (Match match in Regex.Matches(challenge.Parameter, "([a-zA-Z]+)=\"([^\"]*)\"")) {
            var name = match.Groups[1].Value;
            if (name.Equals("realm", StringComparison.OrdinalIgnoreCase)) realm = match.Groups[2].Value;
            else if (name.Equals("service", StringComparison.OrdinalIgnoreCase)) service = match.Groups[2].Value;
        }

        if (!Uri.TryCreate(realm, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("The image registry returned an invalid authentication challenge");
        return new RegistryAuthenticationChallenge(uri, service);
    }

    public Uri GetTokenUri(string repository) {
        var query = "scope=" + Uri.EscapeDataString($"repository:{repository}:pull");
        if (Service is not null) query += "&service=" + Uri.EscapeDataString(Service);
        return new UriBuilder(Realm) {
            Query = string.IsNullOrEmpty(Realm.Query) ? query : Realm.Query[1..] + "&" + query
        }.Uri;
    }
}