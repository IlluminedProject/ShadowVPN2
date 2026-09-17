namespace ShadowVPN2.Entities;

public interface IProtocolDefinition {
    static abstract ProtocolSocketKind SocketKind { get; }
}