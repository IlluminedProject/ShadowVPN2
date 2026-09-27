namespace ShadowVPN2.Contracts.Application;

public interface IClusterApplicationService {
    Task<string> GenerateNodeJoinTokenAsync(string name, string? domain,
        CancellationToken cancellationToken = default);
}