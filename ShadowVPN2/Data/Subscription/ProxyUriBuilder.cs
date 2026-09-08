using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ShadowVPN2.Data.Subscription;

public sealed class ProxyUriBuilder {
    private readonly Dictionary<string, string> _queryParams = new(StringComparer.OrdinalIgnoreCase);

    public ProxyUriBuilder(string scheme, string host, int port) {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        Scheme = scheme;
        Host = host;
        Port = port;
    }

    public string Scheme { get; }
    public string Host { get; }
    public int Port { get; }
    public string? UserName { get; private set; }
    public string? Password { get; private set; }
    public string? Fragment { get; private set; }

    public ProxyUriBuilder WithCredentials(string? userName, string? password = null) {
        UserName = userName;
        Password = password;
        return this;
    }

    public ProxyUriBuilder AddQuery(string key, string? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.IsNullOrEmpty(value))
            _queryParams[key] = value;

        return this;
    }

    public ProxyUriBuilder AddQueryIf(bool condition, string key, string? value) {
        return condition ? AddQuery(key, value) : this;
    }

    public ProxyUriBuilder WithFragment(string? fragment) {
        Fragment = fragment;
        return this;
    }

    public string Build() {
        var builder = new StringBuilder()
            .Append(Scheme)
            .Append("://");

        if (!string.IsNullOrEmpty(UserName) || !string.IsNullOrEmpty(Password)) {
            if (!string.IsNullOrEmpty(UserName)) {
                builder.Append(Uri.EscapeDataString(UserName));
                if (!string.IsNullOrEmpty(Password))
                    builder.Append(':').Append(Uri.EscapeDataString(Password));
            }
            else {
                builder.Append(Uri.EscapeDataString(Password!));
            }

            builder.Append('@');
        }

        builder.Append(FormatHost(Host)).Append(':').Append(Port).Append('/');

        if (_queryParams.Count > 0) {
            builder.Append('?');
            var first = true;
            foreach (var (key, value) in _queryParams) {
                if (!first)
                    builder.Append('&');

                builder.Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        if (!string.IsNullOrEmpty(Fragment))
            builder.Append('#').Append(Uri.EscapeDataString(Fragment));

        return builder.ToString();
    }

    private static string FormatHost(string host) {
        if (host.StartsWith('[') && host.EndsWith(']'))
            return host;

        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            return $"[{host}]";

        return host.Contains(':') ? $"[{host}]" : host;
    }
}