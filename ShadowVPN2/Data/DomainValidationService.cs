using System.Net;
using System.Net.Sockets;
using DnsClient;
using DnsClient.Protocol;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data;

public sealed class DomainValidationService(LookupClient dnsClient, ILogger<DomainValidationService> logger) {
    public async Task<DomainCheckResponse> CheckAsync(HostName? domain, string? publicIpv4, string? publicIpv6,
        CancellationToken cancellationToken = default) {
        var checkedAt = DateTimeOffset.UtcNow;
        if (domain is null)
            return Create(DomainValidationState.NoDomain, null, [], checkedAt);

        try {
            var resolution = await ResolveAsync(domain.Value, cancellationToken);
            if (resolution.Error != null) {
                return new DomainCheckResponse {
                    State = DomainValidationState.LookupFailed,
                    Domain = domain,
                    ResolvedAddresses = [],
                    CheckedAt = checkedAt,
                    Error = resolution.Error
                };
            }

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

    public async Task<DomainResolutionResponse> ResolveAsync(HostName domain,
        CancellationToken cancellationToken = default) {
        try {
            if (IPAddress.TryParse(domain.Value, out var literal)) {
                return new DomainResolutionResponse
                    { Domain = domain, Addresses = [NormalizeAddress(literal).ToString()] };
            }

            // Query DNS records directly: OS hostname resolution also includes /etc/hosts and local interfaces.
            var ipv4 = dnsClient.QueryAsync(domain.Value + ".", QueryType.A, cancellationToken: cancellationToken);
            var ipv6 = dnsClient.QueryAsync(domain.Value + ".", QueryType.AAAA, cancellationToken: cancellationToken);
            var responses = await Task.WhenAll(ipv4, ipv6);
            var addresses = responses.SelectMany(response => response.Answers)
                .Where(record => record is ARecord or AaaaRecord)
                .Select(record => record is ARecord a ? a.Address : ((AaaaRecord)record).Address)
                .Select(NormalizeAddress)
                .Distinct()
                .OrderBy(address => address.AddressFamily)
                .ThenBy(address => address.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(address => address.ToString())
                .ToList();
            return new DomainResolutionResponse { Domain = domain, Addresses = addresses };
        }
        catch (DnsResponseException ex) when (ex.Code == DnsResponseCode.NotExistentDomain) {
            return new DomainResolutionResponse { Domain = domain, Addresses = [] };
        }
        catch (Exception ex) when (ex is DnsResponseException or SocketException or ArgumentException) {
            logger.LogWarning(ex, "DNS lookup failed for {Domain}", domain);
            return new DomainResolutionResponse { Domain = domain, Addresses = [], Error = ex.Message };
        }
    }

    private static DomainCheckResponse Create(DomainValidationState state, HostName? domain,
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