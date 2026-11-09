namespace ShadowVPN2.Data;

public interface INodeStatusCheck {
    string Id { get; }
    Task<NodeCheckResult> CheckAsync(CancellationToken cancellationToken);
}