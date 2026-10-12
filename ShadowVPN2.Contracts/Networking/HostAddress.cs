using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

[JsonConverter(typeof(HostAddressJsonConverter))]
public readonly record struct HostAddress {
    private const int DefaultPort = 443;

    public HostAddress(HostName host, int? port = null) {
        if (string.IsNullOrWhiteSpace(host.Value))
            throw new ArgumentException("Host name is required.", nameof(host));
        if (port.HasValue) {
            ArgumentOutOfRangeException.ThrowIfLessThan(port.Value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port.Value, 65535);
        }

        Host = host;
        Port = port;
    }

    public HostName Host { get; }
    public int? Port { get; }

    public static HostAddress Parse(string address) {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var value = address.Trim();
        if (IPAddress.TryParse(value, out var ip) && !value.StartsWith('['))
            return new HostAddress(HostName.Parse(value));

        int? port = null;
        if (value.StartsWith('[')) {
            var closingBracket = value.IndexOf(']');
            if (closingBracket < 0 || !IPAddress.TryParse(value.AsSpan(1, closingBracket - 1), out ip)
                                   || ip.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("Host is not a valid IPv6 address.", nameof(address));

            if (closingBracket + 1 < value.Length) {
                if (value[closingBracket + 1] != ':')
                    throw new ArgumentException("Host must be an address, optionally with a port.", nameof(address));
                port = ParsePort(value.AsSpan(closingBracket + 2));
            }

            return new HostAddress(HostName.Parse(ip.ToString()), port);
        }

        var separator = value.LastIndexOf(':');
        if (separator >= 0) {
            if (value.IndexOf(':') != separator)
                throw new ArgumentException("IPv6 addresses with a port must be enclosed in brackets.",
                    nameof(address));
            port = ParsePort(value.AsSpan(separator + 1));
            value = value[..separator];
        }

        return new HostAddress(HostName.Parse(value), port);
    }

    public string FormatForUri() {
        return Host.FormatForUri();
    }

    public string FormatWithPort(int? port = null) {
        port ??= Port;
        if (port is null)
            throw new InvalidOperationException("The host address does not contain a port");

        ArgumentOutOfRangeException.ThrowIfLessThan(port.Value, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port.Value, 65535);
        return $"{FormatForUri()}:{port.Value}";
    }


    public override string ToString() {
        return Port is null or DefaultPort
            ? FormatForUri()
            : FormatWithPort();
    }

    private static int ParsePort(ReadOnlySpan<char> value) {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
            throw new ArgumentException("Host port is not valid.");
        return port;
    }
}