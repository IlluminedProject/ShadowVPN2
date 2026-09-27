using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Frontend;

public sealed class FrontendUserContext(
    AuthenticationStateProvider authenticationStateProvider,
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService) {
    public async Task<ApplicationUser> RequireUserAsync(CancellationToken cancellationToken = default) {
        var principal = await GetPrincipalAsync();
        if (principal.Identity?.IsAuthenticated != true)
            throw new ApplicationServiceException(HttpStatusCode.Unauthorized);

        return await userManager.GetRequiredUserAsync(principal);
    }

    public async Task RequirePolicyAsync(string policy, CancellationToken cancellationToken = default) {
        var principal = await GetPrincipalAsync();
        if (principal.Identity?.IsAuthenticated != true)
            throw new ApplicationServiceException(HttpStatusCode.Unauthorized);

        if (!(await authorizationService.AuthorizeAsync(principal, policy)).Succeeded)
            throw new ApplicationServiceException(HttpStatusCode.Forbidden);
    }

    public async Task RequireRoleAsync(string role, CancellationToken cancellationToken = default) {
        var principal = await GetPrincipalAsync();
        if (principal.Identity?.IsAuthenticated != true)
            throw new ApplicationServiceException(HttpStatusCode.Unauthorized);

        if (!principal.IsInRole(role))
            throw new ApplicationServiceException(HttpStatusCode.Forbidden);
    }

    private async Task<ClaimsPrincipal> GetPrincipalAsync() =>
        (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
}