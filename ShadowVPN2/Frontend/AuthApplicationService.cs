using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents.Session;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class AuthApplicationService(
    IAsyncDocumentSession documentSession,
    SignInManager<ApplicationUser> signInManager) : IAuthApplicationService {
    public async Task<AuthOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken = default) {
        var configuration = await documentSession.LoadAsync<EntityGlobalConfiguration>(
            "GlobalConfiguration", cancellationToken);
        var oidc = configuration?.Providers.OfType<OidcAuthProvider>().FirstOrDefault();
        var local = configuration?.Providers.OfType<LocalAuthProvider>().FirstOrDefault();
        var deviceFlowProvider = oidc is { IsEnabled: true, DeviceFlowEnabled: true }
            ? oidc.SchemeName
            : null;
        var externalProviders = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Select(provider => new ExternalLoginProviderResponse {
                Name = provider.Name,
                DisplayName = provider.DisplayName ?? provider.Name,
                UseDeviceFlow = provider.Name == deviceFlowProvider
            })
            .ToArray();

        return new AuthOptionsResponse {
            LocalLoginEnabled = local?.IsEnabled == true,
            ExternalProviders = externalProviders
        };
    }
}