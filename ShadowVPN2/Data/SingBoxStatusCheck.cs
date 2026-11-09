namespace ShadowVPN2.Data;

public sealed class SingBoxStatusCheck(SingBoxService singBoxService) : INodeStatusCheck {
    public const string CheckId = "sing-box";

    public string Id {
        get => CheckId;
    }

    public Task<NodeCheckResult> CheckAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new NodeCheckResult {
            Status = singBoxService.IsRunning ? NodeCheckStatus.Healthy : NodeCheckStatus.Unhealthy,
            CheckedAt = now,
            ValidUntil = now.AddSeconds(90)
        });
    }
}