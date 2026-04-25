using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Identity;
using IdentityRole = Raven.Identity.IdentityRole;
using IdentityUser = Raven.Identity.IdentityUser;

namespace ShadowVPN2.Infrastructure.Authentication;

public class AdvancedUserStore<TUser, TRole> : UserStore<TUser, TRole>, IUserPasskeyStore<TUser>
    where TUser : AdvancedIdentityUser where TRole : IdentityRole, new()
{
    public AdvancedUserStore(Func<IAsyncDocumentSession> getSession, ILogger<AdvancedUserStore<TUser, TRole>> logger, IOptions<RavenDbIdentityOptions> options) : base(getSession, logger, options)
    {
    }

    public AdvancedUserStore(IAsyncDocumentSession session, ILogger<AdvancedUserStore<TUser, TRole>> logger, IOptions<RavenDbIdentityOptions> options) : base(session, logger, options)
    {
    }

    public Task AddOrUpdatePasskeyAsync(TUser user, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        var existing = user.Passkeys.FirstOrDefault(p => p.CredentialId.SequenceEqual(passkey.CredentialId));
        if (existing != null)
        {
            user.Passkeys.Remove(existing);
        }
        user.Passkeys.Add(passkey);
        return Task.CompletedTask;
    }

    public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(TUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult<IList<UserPasskeyInfo>>(user.Passkeys);
    }

    public async Task<TUser?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        return await DbSession.Query<TUser>()
            .FirstOrDefaultAsync(u => u.Passkeys.Any(p => p.CredentialId == credentialId), cancellationToken);
    }

    public Task<UserPasskeyInfo?> FindPasskeyAsync(TUser user, byte[] credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.Passkeys.FirstOrDefault(p => p.CredentialId.SequenceEqual(credentialId)));
    }

    public Task RemovePasskeyAsync(TUser user, byte[] credentialId, CancellationToken cancellationToken)
    {
        user.Passkeys.RemoveAll(p => p.CredentialId.SequenceEqual(credentialId));
        return Task.CompletedTask;
    }
}