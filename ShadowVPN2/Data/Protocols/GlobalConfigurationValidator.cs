using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public static class GlobalConfigurationValidator {
    public static void Validate(EntityGlobalConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ValidateMeshPort(configuration);
        var protocols = configuration.Protocols ?? [];
        var transports = configuration.Transports ?? [];

        var protocolsById = new Dictionary<Guid, ProtocolGlobalSettings>();
        var reservedPorts = new List<(ProtocolSocketKind Kind, PortBinding Binding)>();
        foreach (var protocol in protocols) {
            if (protocol.Id == Guid.Empty || !protocolsById.TryAdd(protocol.Id, protocol))
                throw new ArgumentException("Protocol IDs must be unique and non-empty", nameof(configuration));

            var binding = ValidateBinding(protocol.ListenPort, $"protocol {protocol.Protocol}");
            if (protocol is not Hysteria2GlobalSettings && binding.PortRangeEnd.HasValue)
                throw new ArgumentException("Only Hysteria2 protocols support port ranges", nameof(configuration));
            if (protocol is NaiveProxyGlobalSettings naiveProxy &&
                (string.IsNullOrWhiteSpace(naiveProxy.TlsCertificatePem) ||
                 string.IsNullOrWhiteSpace(naiveProxy.TlsKeyPem))) {
                throw new ArgumentException("NaiveProxy requires a TLS certificate and private key",
                    nameof(configuration));
            }

            var socketKind = ProtocolDefinitionMetadata.GetSocketKind(protocol);
            foreach (var reserved in reservedPorts) {
                if (reserved.Kind != socketKind || !binding.Overlaps(reserved.Binding))
                    continue;

                if (binding.ListenPort == reserved.Binding.ListenPort) {
                    throw new ArgumentException($"Protocol listen port {binding.ListenPort} is used more than once",
                        nameof(configuration));
                }

                throw new ArgumentException("Protocol port range conflicts with another protocol binding",
                    nameof(configuration));
            }

            reservedPorts.Add((socketKind, binding));
        }

        var transportIds = new HashSet<Guid>();
        foreach (var transport in transports) {
            if (transport.Id == Guid.Empty || !transportIds.Add(transport.Id))
                throw new ArgumentException("Transport IDs must be unique and non-empty", nameof(configuration));

            if (!protocolsById.TryGetValue(transport.ProtocolId, out var protocol)) {
                throw new ArgumentException($"Transport {transport.Id} references an unknown protocol",
                    nameof(configuration));
            }

            var binding = ValidateBinding(transport.ListenPort, $"transport {transport.Id}");
            var socketKind = GetTransportSocketKind(transport, protocol);
            foreach (var reserved in reservedPorts) {
                if (reserved.Kind == socketKind && binding.Overlaps(reserved.Binding)) {
                    throw new ArgumentException(
                        $"Transport listen port or range for {binding.ListenPort} conflicts with another port binding",
                        nameof(configuration));
                }
            }

            reservedPorts.Add((socketKind, binding));

            if (transport is FreeTurnTransportSettings freeTurn)
                ValidateFreeTurn(freeTurn);
        }
    }

    public static void ValidateMeshPort(EntityGlobalConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        var meshPort = configuration.AwgSettings.ListenPort.ListenPort;
        ValidatePort(meshPort, "cluster mesh");
        var protocols = configuration.Protocols ?? [];
        if (protocols.Any(protocol =>
                ProtocolDefinitionMetadata.GetSocketKind(protocol) == ProtocolSocketKind.Udp &&
                protocol.ListenPort.Contains(meshPort)) ||
            (configuration.Transports ?? []).Any(transport => {
                var protocol = protocols.FirstOrDefault(candidate => candidate.Id == transport.ProtocolId);
                return protocol is not null &&
                       GetTransportSocketKind(transport, protocol) == ProtocolSocketKind.Udp &&
                       transport.ListenPort.Contains(meshPort);
            })) {
            throw new ArgumentException($"UDP listen port {meshPort} is reserved for cluster mesh",
                nameof(configuration));
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

    private static ProtocolSocketKind GetTransportSocketKind(TransportSettings transport,
        ProtocolGlobalSettings protocol) {
        return transport switch {
            FreeTurnTransportSettings => ProtocolDefinitionMetadata.GetSocketKind(protocol),
            _ => throw new InvalidOperationException($"Unknown transport type {transport.GetType().Name}")
        };
    }

    private static PortBinding ValidateBinding(PortBinding binding, string owner) {
        ValidatePort(binding.ListenPort, owner);
        if (binding.PortRangeEnd is { } end)
            ValidatePort(end, $"{owner} port range end");

        return binding;
    }

    private static void ValidatePort(int port, string owner) {
        if (port is < 1 or > 65535)
            throw new ArgumentException($"{owner} listen port must be between 1 and 65535, got {port}");
    }
}