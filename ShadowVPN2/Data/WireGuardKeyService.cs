using Raven.Client.Documents;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure;

namespace ShadowVPN2.Data;

public sealed class WireGuardKeyService(IDocumentStore documentStore) {
    public async Task EnsureKeyAsync(EntityClient client, CancellationToken ct = default) {
        if (client.WireGuard is not null &&
            !string.IsNullOrWhiteSpace(client.WireGuard.PrivateKey) &&
            !string.IsNullOrWhiteSpace(client.WireGuard.PublicKey))
            return;

        var (privateKey, publicKey) = AwgKeyGenerator.GenerateKeyPair();
        using var session = documentStore.OpenAsyncSession();
        var stored = await session.LoadAsync<EntityClient>(client.Id, ct);
        if (stored is null) return;

        stored.WireGuard ??= new WireGuardClientSettings();
        stored.WireGuard.PrivateKey ??= privateKey;
        stored.WireGuard.PublicKey ??= publicKey;
        client.WireGuard = stored.WireGuard;
        await session.SaveChangesAsync(ct);
    }
}