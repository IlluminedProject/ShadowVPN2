namespace ShadowVPN2.Contracts.Protocols;

public enum ProtocolDomainRouteState {
    Valid,
    PartialMatch,
    NoDomain,
    NoRecords,
    UnknownNode,
    MultipleNodes,
    LookupFailed
}