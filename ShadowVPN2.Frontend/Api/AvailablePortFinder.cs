using ShadowVPN2.Contracts.Protocols;

namespace ShadowVPN2.Frontend.Api;

public static class AvailablePortFinder {
    public static int? Find(int preferred, IEnumerable<ProtocolSettingsDto> protocols,
        IEnumerable<TransportSettingsDto> transports, int? reservedPort) {
        for (var offset = 0; offset < 65535; offset++) {
            var port = (preferred - 1 + offset) % 65535 + 1;
            if (port == reservedPort || transports.Any(transport => transport.PortBinding.Contains(port)) ||
                protocols.Any(protocol => protocol switch {
                    IPortBindingSettings binding => binding.PortBinding.Contains(port),
                    IListenPortSettings listener => listener.ListenPort == port,
                    _ => false
                }))
                continue;
            return port;
        }

        return null;
    }
}