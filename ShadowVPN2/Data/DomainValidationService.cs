using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace ShadowVPN2.Data;

public sealed class DomainValidationService(ILogger<DomainValidationService> logger) {
    public string? Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var domain = value.Trim().TrimEnd('.');
        if (domain.Contains("://", StringComparison.Ordinal) || domain.Contains('/') || domain.Contains(':'))
            throw new ArgumentException("Domain must be a hostname without scheme, path, or port.");
        if (IPAddress.TryParse(domain, out _))
            throw new ArgumentException("Domain cannot be an IP address.");

        try {
            domain = new IdnMapping().GetAscii(domain).ToLowerInvariant();
        }
        catch (ArgumentException) {
            throw new ArgumentException("Domain is not a valid DNS hostname.");
        }

        if (domain.Length > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            throw new ArgumentException("Domain is not a valid DNS hostname.");

        return domain;
    }

    public async Task<DomainCheckResponse> CheckAsync(string? domain, string? publicIpv4, string? publicIpv6,
        CancellationToken cancellationToken = default) {
        var checkedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(domain))
            return Create(DomainValidationState.NoDomain, null, [], checkedAt);

        try {
            var resolution = await ResolveAsync(domain, cancellationToken);
            if (resolution.Error != null)
                return new DomainCheckResponse {
                    State = DomainValidationState.LookupFailed,
                    Domain = domain,
                    ResolvedAddresses = [],
                    CheckedAt = checkedAt,
                    Error = resolution.Error
                };
            var resolved = resolution.Addresses.Select(IPAddress.Parse).ToList();

            if (resolved.Count == 0)
                return Create(DomainValidationState.NoRecords, domain, [], checkedAt);

            var resolvedValues = resolved.Select(address => address.ToString()).ToList();
            var expectedAddresses = new HashSet<IPAddress>();
            AddAddress(expectedAddresses, publicIpv4);
            AddAddress(expectedAddresses, publicIpv6);
            if (expectedAddresses.Count == 0)
                return Create(DomainValidationState.PublicIpUnknown, domain, resolvedValues, checkedAt);

            var matchingCount = resolved.Count(expectedAddresses.Contains);
            var state = matchingCount switch {
                0 => DomainValidationState.Mismatch,
                _ when matchingCount == resolved.Count => DomainValidationState.Valid,
                _ => DomainValidationState.PartialMatch
            };
            return Create(state, domain, resolvedValues, checkedAt);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException) {
            logger.LogWarning(ex, "DNS lookup failed for {Domain}", domain);
            return new DomainCheckResponse {
                State = DomainValidationState.LookupFailed,
                Domain = domain,
                ResolvedAddresses = [],
                CheckedAt = checkedAt,
                Error = ex.Message
            };
        }
    }

    public async Task<DomainResolutionResponse> ResolveAsync(string domain,
        CancellationToken cancellationToken = default) {
        try {
            var addresses = (await Dns.GetHostAddressesAsync(domain, cancellationToken))
                .Select(NormalizeAddress)
                .Distinct()
                .OrderBy(address => address.AddressFamily)
                .ThenBy(address => address.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(address => address.ToString())
                .ToList()
                .AsReadOnly();
            return new DomainResolutionResponse { Domain = domain, Addresses = addresses };
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData) {
            return new DomainResolutionResponse { Domain = domain, Addresses = [] };
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException) {
            logger.LogWarning(ex, "DNS lookup failed for {Domain}", domain);
            return new DomainResolutionResponse { Domain = domain, Addresses = [], Error = ex.Message };
        }
    }

    private static DomainCheckResponse Create(DomainValidationState state, string? domain,
        IReadOnlyList<string> addresses, DateTimeOffset checkedAt) {
        return new DomainCheckResponse {
            State = state,
            Domain = domain,
            ResolvedAddresses = addresses,
            CheckedAt = checkedAt
        };
    }

    private static void AddAddress(HashSet<IPAddress> addresses, string? value) {
        if (IPAddress.TryParse(value, out var address))
            addresses.Add(NormalizeAddress(address));
    }

    private static IPAddress NormalizeAddress(IPAddress address) {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}