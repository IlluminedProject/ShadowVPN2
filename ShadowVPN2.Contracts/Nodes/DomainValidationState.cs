namespace ShadowVPN2.Contracts.Nodes;

public enum DomainValidationState {
    Valid,
    PartialMatch,
    NoDomain,
    PublicIpUnknown,
    NoRecords,
    Mismatch,
    LookupFailed
}