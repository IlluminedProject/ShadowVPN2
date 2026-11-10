using System.Net;
using System.Net.Sockets;
using DnsClient;
using DnsClient.Protocol;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data;

public sealed class DomainValidationService(LookupClient dnsClient, ILogger<DomainValidationService> logger) {
    private const int MaximumAliases = 8;
    private static readonly TimeSpan MaximumFreshness = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(15);

    private static readonly DnsQueryAndServerOptions CachedQueryOptions = new() {
        UseCache = true,
        ThrowDnsErrors = false,
        ContinueOnDnsError = false,
        ContinueOnEmptyResponse = false
    };

    private static readonly DnsQueryAndServerOptions RefreshQueryOptions = new() {
        UseCache = false,
        ThrowDnsErrors = false,
        ContinueOnDnsError = false,
        ContinueOnEmptyResponse = false
    };

    public async Task<DomainCheckResponse> CheckAsync(HostName? domain, string? publicIpv4, string? publicIpv6,
        CancellationToken cancellationToken = default, DateTimeOffset? networkCheckedAt = null,
        bool forceRefresh = false) {
        cancellationToken.ThrowIfCancellationRequested();
        var checkedAt = DateTimeOffset.UtcNow;
        var expectedIpv4 = ParseExpected(publicIpv4, AddressFamily.InterNetwork);
        var expectedIpv6 = ParseExpected(publicIpv6, AddressFamily.InterNetworkV6);
        DnsFamilyResult? a = null;
        DnsFamilyResult? aaaa = null;
        if (domain is not null)
            (a, aaaa) = await ResolveFamiliesAsync(domain.Value, forceRefresh, cancellationToken);
        var validUntil = a is null || aaaa is null ? checkedAt : Min(a.ValidUntil, aaaa.ValidUntil);
        if (networkCheckedAt is { } networkTime)
            validUntil = Min(validUntil, networkTime + NodeNetworkService.NetworkStateMaxAge);
        var addresses = MergeAddresses(a, aaaa);
        var error = JoinErrors(a, aaaa);
        var complete = a is { Error: null } && aaaa is { Error: null };
        var state = domain is null
            ? DomainValidationState.NoDomain
            : networkCheckedAt is { } network && DateTimeOffset.UtcNow - network > NodeNetworkService.NetworkStateMaxAge
                ? DomainValidationState.NetworkStateStale
                : !complete
                    ? DomainValidationState.LookupFailed
                    : a!.NameNotFound || aaaa!.NameNotFound
                        ? DomainValidationState.NameNotFound
                        : addresses.Count == 0
                            ? DomainValidationState.NoRecords
                            : Compare(a!, aaaa!, expectedIpv4, expectedIpv6);
        return new DomainCheckResponse {
            State = state,
            Domain = domain,
            ResolvedAddresses = addresses,
            CheckedAt = checkedAt,
            ExpectedIpv4 = publicIpv4,
            ExpectedIpv6 = publicIpv6,
            NetworkCheckedAt = networkCheckedAt,
            ValidUntil = validUntil,
            A = a,
            Aaaa = aaaa,
            IsComplete = complete,
            Error = error
        };
    }

    public async Task<DomainResolutionResponse> ResolveAsync(HostName domain,
        CancellationToken cancellationToken = default) {
        var (a, aaaa) = await ResolveFamiliesAsync(domain, false, cancellationToken);
        return new DomainResolutionResponse {
            Domain = domain,
            Addresses = MergeAddresses(a, aaaa),
            Error = JoinErrors(a, aaaa)
        };
    }

    private async Task<(DnsFamilyResult A, DnsFamilyResult Aaaa)> ResolveFamiliesAsync(HostName domain,
        bool forceRefresh, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(domain.Value, out var literal)) {
            literal = NormalizeAddress(literal);
            var validUntil = DateTimeOffset.UtcNow + MaximumFreshness;
            var result = new DnsFamilyResult
                { Addresses = [literal.ToString()], CanonicalNames = [], ValidUntil = validUntil };
            var empty = new DnsFamilyResult { Addresses = [], CanonicalNames = [], ValidUntil = validUntil };
            return literal.AddressFamily == AddressFamily.InterNetwork ? (result, empty) : (empty, result);
        }

        var a = GetFamilyAsync(domain.Value, QueryType.A, forceRefresh, cancellationToken);
        var aaaa = GetFamilyAsync(domain.Value, QueryType.AAAA, forceRefresh, cancellationToken);
        await Task.WhenAll(a, aaaa);
        return (await a, await aaaa);
    }

    private async Task<DnsFamilyResult> GetFamilyAsync(string domain, QueryType type, bool forceRefresh,
        CancellationToken cancellationToken) {
        try {
            // One deadline covers every CNAME hop and remains linked to this caller.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(LookupTimeout);
            var result = await ResolveFamilyAsync(domain, type, forceRefresh ? RefreshQueryOptions : CachedQueryOptions,
                timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(ex, "DNS {Type} lookup failed for {Domain}", type, domain);
            return new DnsFamilyResult {
                Addresses = [], CanonicalNames = [], ValidUntil = DateTimeOffset.UtcNow,
                Error = ex is OperationCanceledException ? "DNS lookup timed out." : ex.Message
            };
        }
    }

    private async Task<DnsFamilyResult> ResolveFamilyAsync(string domain, QueryType type,
        DnsQueryAndServerOptions queryOptions, CancellationToken cancellationToken) {
        var current = domain;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { domain };
        var canonicalNames = new List<string>();
        var validUntil = DateTimeOffset.UtcNow + MaximumFreshness;
        while (true) {
            var response = await dnsClient.QueryAsync(new DnsQuestion(current + ".", type), queryOptions,
                cancellationToken);
            var receivedAt = DateTimeOffset.UtcNow;
            var code = response.Header.ResponseCode;
            if (code is not DnsHeaderResponseCode.NoError and not DnsHeaderResponseCode.NotExistentDomain)
                return Result([], $"DNS {type} lookup failed: {code}.");
            var followedAlias = false;
            while (true) {
                var alias = response.Answers.OfType<CNameRecord>()
                    .FirstOrDefault(record => SameName(record.DomainName.Value, current));
                if (alias is null)
                    break;
                if (canonicalNames.Count == MaximumAliases)
                    return Result([], $"DNS CNAME chain exceeds {MaximumAliases} aliases.");
                var target = alias.CanonicalName.Value.TrimEnd('.').ToLowerInvariant();
                if (!visited.Add(target))
                    return Result([], "DNS CNAME loop detected.");
                validUntil = Min(validUntil, receivedAt.AddSeconds(alias.TimeToLive));
                canonicalNames.Add(target);
                current = target;
                followedAlias = true;
            }

            var records = response.Answers.Where(record => SameName(record.DomainName.Value, current) &&
                                                           (type == QueryType.A
                                                               ? record is ARecord
                                                               : record is AaaaRecord)).ToArray();
            if (code == DnsHeaderResponseCode.NoError && records.Length != 0) {
                foreach (var record in records)
                    validUntil = Min(validUntil, receivedAt.AddSeconds(record.TimeToLive));
                return Result(records
                    .Select(record =>
                        NormalizeAddress(record is ARecord a ? a.Address : ((AaaaRecord)record).Address).ToString())
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }

            var authorities = response.Authorities.OfType<SoaRecord>().ToArray();
            if (code == DnsHeaderResponseCode.NotExistentDomain || !followedAlias || authorities.Length != 0) {
                // RFC 2308: a negative answer without SOA has no reusable lifetime.
                var negativeTtl = authorities.Length == 0
                    ? 0
                    : authorities.Min(soa => Math.Min(soa.TimeToLive, soa.Minimum));
                validUntil = Min(validUntil, receivedAt.AddSeconds(negativeTtl));
                return Result([], nameNotFound: code == DnsHeaderResponseCode.NotExistentDomain);
            }
        }

        DnsFamilyResult Result(IReadOnlyList<string> addresses, string? error = null, bool nameNotFound = false) {
            return new DnsFamilyResult {
                Addresses = addresses,
                CanonicalNames = canonicalNames.ToArray(),
                ValidUntil = error is null ? validUntil : DateTimeOffset.UtcNow,
                Error = error,
                NameNotFound = nameNotFound
            };
        }
    }

    private static DomainValidationState Compare(DnsFamilyResult a, DnsFamilyResult aaaa,
        IPAddress? expectedIpv4, IPAddress? expectedIpv6) {
        var matches = 0;
        var mismatches = 0;
        var unknown = false;
        Count(a);
        Count(aaaa);
        return mismatches != 0 ? matches == 0 ? DomainValidationState.Mismatch : DomainValidationState.PartialMatch
            : unknown || matches == 0 ? DomainValidationState.PublicIpUnknown : DomainValidationState.Valid;

        void Count(DnsFamilyResult family) {
            foreach (var value in family.Addresses) {
                var address = IPAddress.Parse(value);
                var expected = address.AddressFamily == AddressFamily.InterNetwork ? expectedIpv4 : expectedIpv6;
                if (expected is null)
                    unknown = true;
                else if (address.Equals(expected))
                    matches++;
                else
                    mismatches++;
            }
        }
    }

    private static IReadOnlyList<string> MergeAddresses(DnsFamilyResult? a, DnsFamilyResult? aaaa) {
        return a is null || aaaa is null
            ? []
            : a.Addresses.Concat(aaaa.Addresses).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? JoinErrors(DnsFamilyResult? a, DnsFamilyResult? aaaa) {
        if (a?.Error is null)
            return aaaa?.Error;
        return aaaa?.Error is null ? a.Error : $"A: {a.Error}; AAAA: {aaaa.Error}";
    }

    private static bool SameName(string left, string right) {
        return left.TrimEnd('.').Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) {
        return first < second ? first : second;
    }

    private static IPAddress? ParseExpected(string? value, AddressFamily family) {
        return IPAddress.TryParse(value, out var address) && NormalizeAddress(address).AddressFamily == family
            ? NormalizeAddress(address)
            : null;
    }

    private static IPAddress NormalizeAddress(IPAddress address) {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}