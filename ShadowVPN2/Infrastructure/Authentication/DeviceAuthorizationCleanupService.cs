namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DeviceAuthorizationCleanupService(
    IServiceScopeFactory scopeFactory,
    DeviceAuthorizationCleanupLease leaseService,
    ILogger<DeviceAuthorizationCleanupService> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                await using var lease = await leaseService.TryAcquireAsync(TimeSpan.FromMinutes(15), stoppingToken);
                if (lease is null)
                    continue;

                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DeviceAuthorizationService>()
                    .CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) {
                logger.LogWarning(exception, "Failed to clean up device authorization transactions");
            }
        }
    }
}