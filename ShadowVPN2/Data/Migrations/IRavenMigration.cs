using Raven.Client.Documents.Session;

namespace ShadowVPN2.Data.Migrations;

public interface IRavenMigration {
    int Version { get; }
    string Description { get; }

    // The runner owns the cluster-wide transaction and commits after every pending migration succeeds.
    Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken);
}