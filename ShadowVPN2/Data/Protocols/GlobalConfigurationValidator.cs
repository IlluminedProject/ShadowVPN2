using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public static class GlobalConfigurationValidator {
    public static void Validate(EntityGlobalConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        var protocols = configuration.Protocols ?? [];
        var transports = configuration.Transports ?? [];

        var protocolIds = new HashSet<Guid>();
        var protocolPorts = new HashSet<(ProtocolSocketKind Kind, int Port)>();
        foreach (var protocol in protocols) {
            if (protocol.Id == Guid.Empty || !protocolIds.Add(protocol.Id))
                throw new ArgumentException("Protocol IDs must be unique and non-empty", nameof(configuration));

            ValidatePort(protocol.ListenPort, $"protocol {protocol.Protocol}");
            var socketKind = ProtocolDefinitionMetadata.GetSocketKind(protocol);
            if (!protocolPorts.Add((socketKind, protocol.ListenPort))) {
                throw new ArgumentException($"Protocol listen port {protocol.ListenPort} is used more than once",
                    nameof(configuration));
            }
        }

        var transportIds = new HashSet<Guid>();
        var transportPorts = new HashSet<int>();
        foreach (var transport in transports) {
            if (transport.Id == Guid.Empty || !transportIds.Add(transport.Id))
                throw new ArgumentException("Transport IDs must be unique and non-empty", nameof(configuration));

            if (!protocolIds.Contains(transport.ProtocolId)) {
                throw new ArgumentException($"Transport {transport.Id} references an unknown protocol",
                    nameof(configuration));
            }

            ValidatePort(transport.ListenPort, $"transport {transport.Id}");
            if (!transportPorts.Add(transport.ListenPort)) {
                throw new ArgumentException($"Transport listen port {transport.ListenPort} is used more than once",
                    nameof(configuration));
            }

            if (transport is FreeTurnTransportSettings &&
                protocolPorts.Contains((ProtocolSocketKind.Udp, transport.ListenPort))) {
                throw new ArgumentException(
                    $"Transport listen port {transport.ListenPort} conflicts with a protocol listen port",
                    nameof(configuration));
            }

            if (transport is FreeTurnTransportSettings freeTurn)
                ValidateFreeTurn(freeTurn);
        }
    }

    private static void ValidateFreeTurn(FreeTurnTransportSettings settings) {
        if (settings.Streams is < 1 or > 64)
            throw new ArgumentException($"FreeTurn streams must be between 1 and 64, got {settings.Streams}");

        if (settings.ObfuscationProfile is null)
            return;

        if (string.IsNullOrWhiteSpace(settings.ObfuscationKey) ||
            settings.ObfuscationKey.Length != 64 ||
            !settings.ObfuscationKey.All(Uri.IsHexDigit)) {
            throw new ArgumentException(
                $"FreeTurn obfuscation key must be 64 hexadecimal characters, got {settings.ObfuscationKey ?? "null"}");
        }
    }

    private static void ValidatePort(int port, string owner) {
        if (port is < 1 or > 65535)
            throw new ArgumentException($"{owner} listen port must be between 1 and 65535, got {port}");
    }
}