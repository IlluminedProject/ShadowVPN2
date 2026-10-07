using ShadowVPN2.Contracts.Auth;

namespace ShadowVPN2.Contracts.Application;

public interface IAuthApplicationService {
    Task<AuthOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken = default);
}