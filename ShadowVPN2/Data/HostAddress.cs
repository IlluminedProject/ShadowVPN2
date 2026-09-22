using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Data;

[JsonConverter(typeof(HostAddressJsonConverter))]
public readonly record struct HostAddress {
    private const int DefaultPort = 443;

    private HostAddress(string value, int? port) {
        Value = value;
        Port = port;
    }

    public string Value { get; }
    public int? Port { get; }

    public static HostAddress Parse(string address) {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var value = address.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return Create(uri.Host, GetPort(uri, value));

        if (Uri.TryCreate($"https://{value}", UriKind.Absolute, out uri) && !string.IsNullOrEmpty(uri.Host))
            return Create(uri.Host, GetPort(uri, value));

        if (IPAddress.TryParse(value, out var ip))
            return Create(ip.ToString(), null);

        return Create(value, null);
    }

    public string FormatForUri() {
        return IsIpv6() ? $"[{Value}]" : Value;
    }

    public string FormatWithPort(int? port = null) {
        port ??= Port;
        if (port is null)
            throw new InvalidOperationException("The host address does not contain a port");

        ArgumentOutOfRangeException.ThrowIfLessThan(port.Value, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port.Value, 65535);
        return $"{FormatForUri()}:{port.Value}";
    }

    public static implicit operator string(HostAddress address) {
        return address.ToString();
    }

    public override string ToString() {
        return Port is null or DefaultPort
            ? FormatForUri()
            : FormatWithPort();
    }

    private static HostAddress Create(string value, int? port) {
        return new HostAddress(value.Trim('[', ']'), port);
    }

    private static int? GetPort(Uri uri, string address) {
        if (uri.Port < 0)
            return null;

        var authority = address;
        var schemeSeparator = authority.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
            authority = authority[(schemeSeparator + 3)..];

        var pathStart = authority.IndexOfAny(['/', '?', '#']);
        if (pathStart >= 0)
            authority = authority[..pathStart];

        var userInfoSeparator = authority.LastIndexOf('@');
        if (userInfoSeparator >= 0)
            authority = authority[(userInfoSeparator + 1)..];

        var hasPort = authority.StartsWith('[')
            ? authority.IndexOf(']') is var closingBracket && closingBracket >= 0 &&
              authority.Length > closingBracket + 1 && authority[closingBracket + 1] == ':'
            : authority.LastIndexOf(':') >= 0;

        return hasPort ? uri.Port : null;
    }

    private bool IsIpv6() {
        return IPAddress.TryParse(Value, out var ip) &&
               ip.AddressFamily == AddressFamily.InterNetworkV6;
    }
}

public sealed class HostAddressJsonConverter : JsonConverter<HostAddress> {
    public override HostAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return HostAddress.Parse(reader.GetString() ?? throw new JsonException("Host address cannot be null"));
    }

    public override void Write(Utf8JsonWriter writer, HostAddress value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.ToString());
    }
}