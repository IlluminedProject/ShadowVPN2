using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Documents.Session;

namespace ShadowVPN2.Data.Migrations;

public sealed class RavenMigrationLock(
    IDocumentStore documentStore,
    ILogger<RavenMigrationLock> logger) {
    internal const string LockKey = "ShadowVPN2/Migrations/Lock";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    public async Task<Lease> AcquireAsync(CancellationToken cancellationToken) {
        var token = Guid.NewGuid().ToString("N");

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<LeaseValue>(LockKey), token: cancellationToken);

            if (current is not null && current.Value.ExpiresAtUtc > DateTime.UtcNow) {
                await Task.Delay(RetryInterval, cancellationToken);
                continue;
            }

            var value = new LeaseValue(token, DateTime.UtcNow.Add(LeaseDuration), Guid.NewGuid().ToString("N"));
            var result = await documentStore.Operations.SendAsync(
                new PutCompareExchangeValueOperation<LeaseValue>(LockKey, value, current?.Index ?? 0),
                token: cancellationToken);

            if (result.Successful)
                return new Lease(documentStore, logger, token);
        }
    }

    public sealed class Lease : IAsyncDisposable {
        private readonly IDocumentStore _documentStore;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _ownershipLostCancellation = new();
        private readonly CancellationTokenSource _renewalCancellation = new();
        private readonly Task _renewalTask;
        private readonly string _token;
        private int _disposed;

        internal Lease(IDocumentStore documentStore, ILogger logger, string token) {
            _documentStore = documentStore;
            _logger = logger;
            _token = token;
            _renewalTask = RenewAsync();
        }

        public CancellationToken OwnershipLostToken {
            get => _ownershipLostCancellation.Token;
        }

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _renewalCancellation.Cancel();
            await _renewalTask;

            try {
                var current = await _documentStore.Operations.SendAsync(
                    new GetCompareExchangeValueOperation<LeaseValue>(LockKey), token: CancellationToken.None);
                if (current?.Value?.Token != _token)
                    return;

                await _documentStore.Operations.SendAsync(
                    new DeleteCompareExchangeValueOperation<LeaseValue>(LockKey, current.Index),
                    token: CancellationToken.None);
            }
            catch (Exception exception) {
                _logger.LogError(exception, "Failed to release RavenDB migration lock");
            }
            finally {
                _renewalCancellation.Dispose();
                _ownershipLostCancellation.Dispose();
            }
        }

        public void ThrowIfOwnershipLost() {
            if (_ownershipLostCancellation.IsCancellationRequested)
                throw new InvalidOperationException("The RavenDB migration lock was lost");
        }

        public async Task FenceCommitAsync(IAsyncDocumentSession session) {
            ThrowIfOwnershipLost();

            var lockValue = await session.Advanced.ClusterTransaction
                .GetCompareExchangeValueAsync<LeaseValue>(LockKey);
            if (lockValue?.Value.Token != _token)
                throw new InvalidOperationException("The RavenDB migration lock is no longer owned by this process");

            lockValue.Value = lockValue.Value with {
                ExpiresAtUtc = DateTime.UtcNow.Add(LeaseDuration),
                RenewalId = Guid.NewGuid().ToString("N")
            };
        }

        public async Task StopRenewalAsync() {
            _renewalCancellation.Cancel();
            await _renewalTask;
            ThrowIfOwnershipLost();
        }

        private async Task RenewAsync() {
            try {
                while (true) {
                    await Task.Delay(RenewalInterval, _renewalCancellation.Token);

                    var current = await _documentStore.Operations.SendAsync(
                        new GetCompareExchangeValueOperation<LeaseValue>(LockKey),
                        token: _renewalCancellation.Token);

                    if (current?.Value?.Token != _token) {
                        LoseOwnership();
                        return;
                    }

                    var value = new LeaseValue(_token, DateTime.UtcNow.Add(LeaseDuration),
                        Guid.NewGuid().ToString("N"));
                    var result = await _documentStore.Operations.SendAsync(
                        new PutCompareExchangeValueOperation<LeaseValue>(LockKey, value, current.Index),
                        token: _renewalCancellation.Token);

                    if (!result.Successful) {
                        LoseOwnership();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_renewalCancellation.IsCancellationRequested) {
            }
            catch (Exception exception) {
                _logger.LogError(exception, "Failed to renew RavenDB migration lock");
                LoseOwnership();
            }
        }

        private void LoseOwnership() {
            _ownershipLostCancellation.Cancel();
            _logger.LogError("RavenDB migration lock ownership lost");
        }
    }

    internal sealed record LeaseValue(string Token, DateTime ExpiresAtUtc, string RenewalId);
}