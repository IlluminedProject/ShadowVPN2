namespace ShadowVPN2.Data.Migrations;

internal sealed class RavenMigrationState {
    public int Version { get; set; }
    public List<RavenMigrationHistoryEntry> AppliedMigrations { get; set; } = [];
}

internal sealed class RavenMigrationHistoryEntry {
    public int Version { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }
}