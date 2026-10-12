using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Application;

public interface IClusterApplicationService {
    Task<string> GenerateNodeJoinTokenAsync(string name, HostName? domain,
        CancellationToken cancellationToken = default);
}