namespace ShadowVPN2.Data;

public enum DomainValidationState {
    Valid,
    PartialMatch,
    NoDomain,
    PublicIpUnknown,
    NoRecords,
    Mismatch,
    LookupFailed
}