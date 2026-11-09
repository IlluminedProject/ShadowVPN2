using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Subscription;

public sealed record SubscriptionGenerationContext(bool StablePorts, string? HardwareId = null) {
    public static SubscriptionGenerationContext Random { get; } = new(false);

    public int SelectWireGuardPort(PortBinding binding, string clientId, Guid protocolId, HostName host) {
        var end = binding.PortRangeEnd ?? binding.ListenPort;
        if (end == binding.ListenPort)
            return binding.ListenPort;
        if (!StablePorts)
            return RandomNumberGenerator.GetInt32(binding.ListenPort, end + 1);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(clientId);
        Append(protocolId.ToString("N"));
        Append(host.Value);
        Append(string.IsNullOrWhiteSpace(HardwareId) ? string.Empty : HardwareId);
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return binding.ListenPort + (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) %
                                          (ulong)(end - binding.ListenPort + 1));

        void Append(string value) {
            var count = Encoding.UTF8.GetByteCount(value);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, count);
            hash.AppendData(length);
            byte[]? rented = null;
            var bytes = count <= 256
                ? stackalloc byte[count]
                : (rented = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count);
            try {
                Encoding.UTF8.GetBytes(value, bytes);
                hash.AppendData(bytes);
            }
            finally {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}