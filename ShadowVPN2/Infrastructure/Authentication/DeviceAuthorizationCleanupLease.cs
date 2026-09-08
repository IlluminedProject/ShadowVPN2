using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;

namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DeviceAuthorizationCleanupLease(IDocumentStore documentStore) {
    private const string LeaseKey = "ShadowVPN2/DeviceAuthorizationCleanupLease";

    public async Task<Lease?> TryAcquireAsync(TimeSpan lifetime, CancellationToken cancellationToken) {
        var value = new LeaseValue(Guid.NewGuid().ToString("N"), DateTime.UtcNow.Add(lifetime));
        var current = await documentStore.Operations.SendAsync(
            new GetCompareExchangeValueOperation<LeaseValue>(LeaseKey), token: cancellationToken);
        if (current is not null && current.Value.ExpiresAtUtc > DateTime.UtcNow)
            return null;

        var result = await documentStore.Operations.SendAsync(
            new PutCompareExchangeValueOperation<LeaseValue>(LeaseKey, value, current?.Index ?? 0),
            token: cancellationToken);

        return result.Successful ? new Lease(documentStore, result.Index, value.Token) : null;
    }

    public sealed class Lease : IAsyncDisposable {
        private readonly IDocumentStore documentStore;
        private readonly long index;
        private readonly string token;
        private int released;

        internal Lease(IDocumentStore documentStore, long index, string token) {
            this.documentStore = documentStore;
            this.index = index;
            this.token = token;
        }

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref released, 1) != 0)
                return;

            var current = await documentStore.Operations.SendAsync(
                new GetCompareExchangeValueOperation<LeaseValue>(LeaseKey), token: CancellationToken.None);
            if (current?.Value?.Token != token)
                return;

            await documentStore.Operations.SendAsync(
                new DeleteCompareExchangeValueOperation<LeaseValue>(LeaseKey, index), token: CancellationToken.None);
        }
    }

    public sealed record LeaseValue(string Token, DateTime ExpiresAtUtc);
}