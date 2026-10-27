using Raven.Client.Documents.Session;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.RavenDb.Migrations;

namespace ShadowVPN2.Data.Migrations;

public sealed class ClusterUpdateDocumentMigration : IRavenMigration {
    public int Version {
        get => 2;
    }

    public string Description {
        get => "Move cluster update state into a cluster-wide document";
    }

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var legacy = await session.Advanced.ClusterTransaction
            .GetCompareExchangeValueAsync<ClusterUpdateState>(ClusterUpdateDocument.DocumentId, cancellationToken);
        var document = await session.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId,
            cancellationToken);
        if (legacy is null)
            return;

        if (document is not null) {
            throw new InvalidOperationException(
                $"Cannot migrate cluster update state: both the legacy compare-exchange entry and document '{ClusterUpdateDocument.DocumentId}' exist; resolve the conflicting state before restarting");
        }

        if (legacy.Value is null) {
            throw new InvalidOperationException(
                "Cannot migrate cluster update state: the legacy compare-exchange entry has no state");
        }

        await session.StoreAsync(new ClusterUpdateDocument { State = legacy.Value }, ClusterUpdateDocument.DocumentId,
            cancellationToken);
        session.Advanced.ClusterTransaction.DeleteCompareExchangeValue(legacy);
    }
}