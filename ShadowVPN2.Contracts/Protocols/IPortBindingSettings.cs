using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Protocols;

public interface IPortBindingSettings {
    PortBinding PortBinding { get; set; }
}