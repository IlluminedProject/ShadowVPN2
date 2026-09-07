namespace ShadowVPN2.Data.Certificates;

public sealed class ManagedCertificateResponse {
    public required Guid NodeId { get; init; }
    public required IReadOnlyList<string> Domains { get; init; }
    public required DateTimeOffset NotBefore { get; init; }
    public required DateTimeOffset NotAfter { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public string? LastError { get; init; }
}