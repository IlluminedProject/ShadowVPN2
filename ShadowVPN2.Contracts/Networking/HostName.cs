using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

/// <summary>
/// Canonical DNS name or IP literal stored as a JSON string across models, DTOs and UI.
/// ToAddress adds an optional connection port.
/// </summary>
[TypeConverter(typeof(HostNameTypeConverter))]
[JsonConverter(typeof(HostNameJsonConverter))]
public readonly record struct HostName {
    private HostName(string value) {
        Value = value;
    }

    public string Value { get; }

    /// <summary>Parses a DNS name or IP literal; ports, URI brackets and URLs are rejected.</summary>
    public static HostName Parse(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim().TrimEnd('.');
        if (value.Contains('[') || value.Contains(']'))
            throw new ArgumentException("Host name must not contain URI brackets.", nameof(value));
        if (IPAddress.TryParse(value, out var ip))
            return new HostName(ip.ToString());

        try {
            value = new IdnMapping().GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException) {
            throw new ArgumentException("Host is not a valid DNS hostname.", nameof(value));
        }

        if (value.Length > 253 || Uri.CheckHostName(value) != UriHostNameType.Dns)
            throw new ArgumentException("Host is not a valid DNS hostname.", nameof(value));
        return new HostName(value);
    }

    public static HostName? ParseOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }

    public HostAddress ToAddress(int? port = null) => new(this, port);

    public string FormatForUri() {
        return IPAddress.TryParse(Value, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{Value}]"
            : Value;
    }

    public override string ToString() => Value;
}