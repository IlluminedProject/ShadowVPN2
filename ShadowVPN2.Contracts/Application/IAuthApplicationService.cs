using ShadowVPN2.Contracts.Auth;

namespace ShadowVPN2.Contracts.Application;

public interface IAuthApplicationService {
    Task<AuthOptionsResponse> GetOptionsAsync(string host, CancellationToken cancellationToken = default);
}