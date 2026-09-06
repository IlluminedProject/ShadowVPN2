namespace ShadowVPN2.Data.Protocols;

public enum ProtocolDomainRouteState {
    Valid,
    PartialMatch,
    NoDomain,
    NoRecords,
    UnknownNode,
    MultipleNodes,
    LookupFailed
}