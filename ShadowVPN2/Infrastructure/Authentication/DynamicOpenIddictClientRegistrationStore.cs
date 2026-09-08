using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DynamicOpenIddictClientRegistrationStore(
    IOptionsMonitorCache<OpenIddictClientOptions> optionsCache) {
    private readonly Dictionary<string, OpenIddictClientRegistration> registrations = new(StringComparer.Ordinal);
    private readonly Lock syncRoot = new();

    public IReadOnlyList<OpenIddictClientRegistration> GetRegistrations() {
        lock (syncRoot) {
            return registrations.Values.ToArray();
        }
    }

    public void AddOrUpdate(OidcAuthProvider provider) {
        ArgumentException.ThrowIfNullOrEmpty(provider.SchemeName);
        if (!Uri.TryCreate(provider.Authority.TrimEnd('/') + '/', UriKind.Absolute, out var issuer) ||
            issuer.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The OIDC authority must be an absolute HTTP(S) URI.");

        var registration = new OpenIddictClientRegistration {
            RegistrationId = provider.SchemeName,
            ProviderName = provider.SchemeName,
            ProviderDisplayName = provider.DisplayName,
            Issuer = issuer,
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecret,
            ClientType = string.IsNullOrWhiteSpace(provider.ClientSecret)
                ? OpenIddictConstants.ClientTypes.Public
                : OpenIddictConstants.ClientTypes.Confidential
        };

        var scopes = provider.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Length == 0)
            scopes = ["openid", "email", "profile"];

        foreach (var scope in scopes)
            registration.Scopes.Add(scope);

        lock (syncRoot) {
            registrations[provider.SchemeName] = registration;
        }

        optionsCache.TryRemove(Options.DefaultName);
    }

    public void Remove(string schemeName) {
        lock (syncRoot) {
            registrations.Remove(schemeName);
        }

        optionsCache.TryRemove(Options.DefaultName);
    }
}