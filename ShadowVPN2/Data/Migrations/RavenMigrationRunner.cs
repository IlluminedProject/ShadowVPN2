using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;

namespace ShadowVPN2.Data.Migrations;

public sealed class RavenMigrationRunner(
    IDocumentStore documentStore,
    IEnumerable<IRavenMigration> migrations,
    RavenMigrationLock migrationLock,
    ILogger<RavenMigrationRunner> logger) {
    private const string StateDocumentId = "ShadowVPN2/Migrations/State";

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default) {
        var orderedMigrations = migrations
            .OrderBy(migration => migration.Version)
            .ToArray();
        ValidateMigrations(orderedMigrations);
        if (orderedMigrations.Length == 0)
            return false;

        using var session = documentStore.OpenAsyncSession();
        var state = await session.LoadAsync<RavenMigrationState>(StateDocumentId, cancellationToken)
                    ?? new RavenMigrationState();
        return GetPendingMigrations(orderedMigrations, state).Count != 0;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default) {
        var orderedMigrations = migrations
            .OrderBy(migration => migration.Version)
            .ToArray();
        ValidateMigrations(orderedMigrations);

        if (orderedMigrations.Length == 0 || !await HasPendingMigrationsAsync(cancellationToken))
            return;

        await using var lease = await migrationLock.AcquireAsync(cancellationToken);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.OwnershipLostToken);

        using var session = documentStore.OpenAsyncSession(new SessionOptions {
            TransactionMode = TransactionMode.ClusterWide
        });

        var state = await session.LoadAsync<RavenMigrationState>(StateDocumentId, linkedCancellation.Token);
        var isNewState = state is null;
        state ??= new RavenMigrationState();
        var pendingMigrations = GetPendingMigrations(orderedMigrations, state);
        if (pendingMigrations.Count == 0) {
            logger.LogInformation("RavenDB migrations are up to date at version {Version}", state.Version);
            return;
        }

        try {
            foreach (var migration in pendingMigrations) {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                logger.LogInformation("Applying RavenDB migration {Version}: {Description}",
                    migration.Version, migration.Description);

                await migration.ApplyAsync(session, linkedCancellation.Token);

                state.Version = migration.Version;
                state.AppliedMigrations.Add(new RavenMigrationHistoryEntry {
                    Version = migration.Version,
                    Description = migration.Description,
                    AppliedAtUtc = DateTime.UtcNow
                });
            }

            if (isNewState)
                await session.StoreAsync(state, StateDocumentId, linkedCancellation.Token);

            await lease.StopRenewalAsync();
            await lease.FenceCommitAsync(session);

            await session.SaveChangesAsync(linkedCancellation.Token);
            logger.LogInformation("Applied {MigrationCount} RavenDB migrations through version {Version}",
                pendingMigrations.Count, state.Version);
        }
        catch (Exception exception) {
            logger.LogError(exception,
                "RavenDB migration failed; the cluster-wide transaction was rolled back and startup will stop");
            throw new InvalidOperationException("RavenDB migrations failed; application startup is aborted", exception);
        }
    }

    private static IReadOnlyList<IRavenMigration> GetPendingMigrations(
        IReadOnlyList<IRavenMigration> orderedMigrations, RavenMigrationState state) {
        if (state.Version > orderedMigrations[^1].Version) {
            throw new InvalidOperationException(
                $"The RavenDB schema is at version {state.Version}, newer than this application supports");
        }

        var appliedVersions = state.AppliedMigrations
            .Select(entry => entry.Version)
            .ToHashSet();
        var missingAppliedVersion = orderedMigrations
            .FirstOrDefault(migration =>
                migration.Version <= state.Version && !appliedVersions.Contains(migration.Version));
        if (missingAppliedVersion is not null) {
            throw new InvalidOperationException(
                $"RavenDB migration history is inconsistent at version {missingAppliedVersion.Version}");
        }

        return orderedMigrations
            .Where(migration => !appliedVersions.Contains(migration.Version))
            .ToArray();
    }

    private static void ValidateMigrations(IReadOnlyList<IRavenMigration> orderedMigrations) {
        for (var i = 0; i < orderedMigrations.Count; i++) {
            var migration = orderedMigrations[i];
            if (migration.Version <= 0)
                throw new InvalidOperationException("RavenDB migration versions must be greater than zero");

            if (i > 0 && migration.Version == orderedMigrations[i - 1].Version)
                throw new InvalidOperationException($"Duplicate RavenDB migration version {migration.Version}");

            if (string.IsNullOrWhiteSpace(migration.Description))
                throw new InvalidOperationException($"RavenDB migration {migration.Version} must have a description");
        }
    }
}