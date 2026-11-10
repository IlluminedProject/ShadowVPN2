using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using DnsClient;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DomainValidationServiceTests {
    private static readonly HostName Domain = HostName.Parse("published.example");

    [Fact]
    public async Task Check_should_report_missing_domain() {
        await using var server = new DnsServer(_ => new Reply());
        var result = await server.Service.CheckAsync(null, null, null);

        result.State.Should().Be(DomainValidationState.NoDomain);
        result.ResolvedAddresses.Should().BeEmpty();
        result.MatchesNode.Should().BeNull();
        server.TotalQueries.Should().Be(0);
    }

    [Theory]
    [InlineData("191.217.251.72", null, 0, DomainValidationState.Valid)]
    [InlineData("191.217.251.72", "2001:db8::1", 0, DomainValidationState.Valid)]
    [InlineData("191.217.251.72", "2001:db8::2", 0, DomainValidationState.PartialMatch)]
    [InlineData("203.0.113.1", null, 0, DomainValidationState.Mismatch)]
    [InlineData(null, null, 0, DomainValidationState.NoRecords)]
    [InlineData(null, null, 3, DomainValidationState.NameNotFound)]
    [InlineData("191.217.251.72", null, 2, DomainValidationState.LookupFailed)]
    public async Task Check_should_use_DNS_records_for_local_hostname(string? ipv4, string? ipv6,
        int responseCode, DomainValidationState expectedState) {
        await using var server = new DnsServer(query => new Reply(responseCode,
            AddressAnswers(query, query.Type == 1 ? ipv4 : ipv6)));
        var result = await server.Service.CheckAsync(HostName.Parse(Environment.MachineName), "191.217.251.72",
            "2001:db8::1");

        result.State.Should().Be(expectedState);
        result.ResolvedAddresses.Should().BeEquivalentTo(responseCode == 0
            ? new[] { ipv4, ipv6 }.OfType<string>()
            : Array.Empty<string>());
        server.TotalQueries.Should().Be(2);
    }

    [Theory]
    [InlineData("191.217.251.72", null, DomainValidationState.PublicIpUnknown)]
    [InlineData(null, "2001:db8::1", DomainValidationState.PublicIpUnknown)]
    [InlineData("203.0.113.9", null, DomainValidationState.Mismatch)]
    [InlineData(null, "2001:db8::9", DomainValidationState.Mismatch)]
    public async Task Unknown_expected_family_should_not_be_a_mismatch_or_hide_known_mismatch(
        string? ipv4, string? ipv6, DomainValidationState state) {
        await using var server = new DnsServer(query => new Reply(Answers:
            AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1")));
        var result = await server.Service.CheckAsync(Domain, ipv4, ipv6);

        result.State.Should().Be(state);
        result.MatchesNode.Should().Be(state == DomainValidationState.Mismatch ? false : null);
    }

    [Theory]
    [InlineData("191.217.251.72", null, DomainValidationState.Valid)]
    [InlineData("191.217.251.72", "2001:db8::1", DomainValidationState.Valid)]
    [InlineData("203.0.113.9", null, DomainValidationState.Mismatch)]
    [InlineData("203.0.113.9", "2001:db8::1", DomainValidationState.Mismatch)]
    [InlineData(null, "2001:db8::1", DomainValidationState.PublicIpUnknown)]
    public async Task Mapped_AAAA_should_compare_with_normalized_IPv4_family(
        string? ipv4, string? ipv6, DomainValidationState expectedState) {
        await using var server = new DnsServer(query => new Reply(Answers:
            AddressAnswers(query, query.Type == 28 ? "::ffff:191.217.251.72" : null),
            Authorities: [Soa(60, 60)]));
        var result = await server.Service.CheckAsync(Domain, ipv4, ipv6);

        result.State.Should().Be(expectedState);
        result.ResolvedAddresses.Should().Equal("191.217.251.72");
        result.Aaaa!.Addresses.Should().Equal("191.217.251.72");
        result.MatchesNode.Should().Be(expectedState == DomainValidationState.PublicIpUnknown
            ? null
            : expectedState == DomainValidationState.Valid);
        server.TotalQueries.Should().Be(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public async Task Family_failure_should_preserve_the_successful_family(int failedType) {
        await using var server = new DnsServer(query => query.Type == failedType
            ? new Reply(2)
            : new Reply(Answers: AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1")));
        var result = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");
        var resolution = await server.Service.ResolveAsync(Domain);

        result.State.Should().Be(DomainValidationState.LookupFailed);
        result.IsComplete.Should().BeFalse();
        result.MatchesNode.Should().BeNull();
        result.ResolvedAddresses.Should().Equal(failedType == 1 ? "2001:db8::1" : "191.217.251.72");
        (failedType == 1 ? result.A : result.Aaaa)!.Error.Should().NotBeNullOrEmpty();
        (failedType == 1 ? result.Aaaa : result.A)!.Error.Should().BeNull();
        resolution.Addresses.Should().Equal(result.ResolvedAddresses);
        resolution.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cname_should_follow_separate_responses_and_use_minimum_chain_ttl() {
        await using var server = new DnsServer(query => query.Name switch {
            "published.example" => new Reply(Answers: [Alias(query.Name, "middle.example", 120)]),
            "middle.example" => new Reply(Answers: [Alias(query.Name, "target.example", 45)]),
            _ => new Reply(Answers: AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1", 300))
        });
        var started = DateTimeOffset.UtcNow;
        var result = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");

        result.State.Should().Be(DomainValidationState.Valid);
        result.A!.CanonicalNames.Should().Equal("middle.example", "target.example");
        result.Aaaa!.CanonicalNames.Should().Equal("middle.example", "target.example");
        result.ValidUntil.Should().BeOnOrAfter(started.AddSeconds(43));
        result.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(45));
        server.TotalQueries.Should().Be(6);
    }

    [Fact]
    public async Task Cname_should_consume_bundled_chain_and_ignore_unrelated_addresses() {
        await using var server = new DnsServer(query => new Reply(Answers: [
            Alias(query.Name, "middle.example", 200), Alias("middle.example", "target.example", 100),
            .. AddressAnswers(query with { Name = "target.example" },
                query.Type == 1 ? "191.217.251.72" : "2001:db8::1"),
            .. AddressAnswers(query with { Name = "unrelated.example" },
                query.Type == 1 ? "203.0.113.9" : "2001:db8::9")
        ]));
        var result = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");

        result.State.Should().Be(DomainValidationState.Valid);
        result.ResolvedAddresses.Should().Equal("191.217.251.72", "2001:db8::1");
        server.TotalQueries.Should().Be(2);
    }

    [Fact]
    public async Task Cname_loop_should_be_reported_for_each_family() {
        await using var server = new DnsServer(query => new Reply(Answers: [
            Alias(query.Name,
                query.Name == Domain.Value ? "loop.example" : Domain.Value, 60)
        ]));
        var result = await server.Service.CheckAsync(Domain, "191.217.251.72", null);

        result.State.Should().Be(DomainValidationState.LookupFailed);
        result.A!.Error.Should().Contain("loop");
        result.Aaaa!.Error.Should().Contain("loop");
        server.TotalQueries.Should().Be(4);
    }

    [Theory]
    [InlineData(8, DomainValidationState.Valid)]
    [InlineData(9, DomainValidationState.LookupFailed)]
    public async Task Cname_should_allow_eight_aliases_and_reject_ninth(int aliases, DomainValidationState state) {
        await using var server = new DnsServer(query => {
            var index = query.Name == Domain.Value ? 0 : int.Parse(query.Name.Split('.')[0][1..]);
            return index < aliases
                ? new Reply(Answers: [Alias(query.Name, $"n{index + 1}.example", 60)])
                : new Reply(Answers: AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1"));
        });
        var result = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");

        result.State.Should().Be(state);
        result.A!.CanonicalNames.Should().HaveCount(8);
        server.TotalQueries.Should().Be(18);
    }

    [Theory]
    [InlineData(0, 90, 20, 20, DomainValidationState.NoRecords)]
    [InlineData(3, 90, 20, 20, DomainValidationState.NameNotFound)]
    [InlineData(3, 12, 90, 12, DomainValidationState.NameNotFound)]
    [InlineData(0, 900, 900, 300, DomainValidationState.NoRecords)]
    public async Task Negative_answers_should_preserve_response_code_and_soa_minimum_ttl(
        int code, int ttl, int minimum, int expectedTtl, DomainValidationState state) {
        await using var server = new DnsServer(_ => new Reply(code, Authorities: [Soa(ttl, minimum)]));
        var started = DateTimeOffset.UtcNow;
        var first = await server.Service.CheckAsync(Domain, "191.217.251.72", null);

        first.State.Should().Be(state);
        first.A!.NameNotFound.Should().Be(code == 3);
        first.Aaaa!.NameNotFound.Should().Be(code == 3);
        first.IsComplete.Should().BeTrue();
        first.MatchesNode.Should().BeFalse();
        first.ValidUntil.Should().BeOnOrAfter(started.AddSeconds(expectedTtl - 2));
        first.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(expectedTtl));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Negative_answer_without_soa_should_have_no_freshness(int code) {
        await using var server = new DnsServer(_ => new Reply(code));
        var first = await server.Service.CheckAsync(Domain, null, null);

        first.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        first.MatchesNode.Should().BeNull();
    }

    [Fact]
    public async Task Native_cache_should_reuse_positive_records_independently_per_family() {
        await using var server = new DnsServer(query => new Reply(Answers:
            AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1", query.Type == 1 ? 900 : 0)));
        var started = DateTimeOffset.UtcNow;
        var first = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");
        await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");

        first.A!.ValidUntil.Should().BeOnOrAfter(started.AddSeconds(298));
        first.A.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddMinutes(5));
        first.Aaaa!.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        first.MatchesNode.Should().BeNull();
        server.Count(Domain.Value, 1).Should().Be(1);
        server.Count(Domain.Value, 28).Should().Be(2);
    }

    [Fact]
    public async Task Force_refresh_should_bypass_native_cache_for_both_families() {
        var changed = false;
        await using var server = new DnsServer(query => new Reply(Answers: AddressAnswers(query,
            query.Type == 1 ? changed ? "203.0.113.9" : "191.217.251.72"
            : changed ? "2001:db8::9" : "2001:db8::1")));
        var first = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");
        changed = true;
        var cached = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1");
        var refreshed = await server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1", forceRefresh: true);

        first.State.Should().Be(DomainValidationState.Valid);
        cached.ResolvedAddresses.Should().Equal(first.ResolvedAddresses);
        refreshed.State.Should().Be(DomainValidationState.Mismatch);
        refreshed.ResolvedAddresses.Should().Equal("203.0.113.9", "2001:db8::9");
        server.TotalQueries.Should().Be(4);
    }

    [Fact]
    public async Task Caller_cancellation_should_cancel_its_lookup_without_canceling_an_independent_refresh() {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        await using var server = new DnsServer(async query => {
            var current = Interlocked.Increment(ref count);
            if (current == 2)
                received.TrySetResult();
            if (current == 4)
                refreshReceived.TrySetResult();
            await release.Task;
            return new Reply(Answers: AddressAnswers(query, query.Type == 1 ? "191.217.251.72" : "2001:db8::1"));
        });
        using var caller = new CancellationTokenSource();
        var first = server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1", caller.Token);
        try {
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = server.Service.CheckAsync(Domain, "191.217.251.72", "2001:db8::1", forceRefresh: true);
            await refreshReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
            release.TrySetResult();
            var result = await second.WaitAsync(TimeSpan.FromSeconds(5));
            result.State.Should().Be(DomainValidationState.Valid);
            server.TotalQueries.Should().Be(4);
        }
        finally {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task Network_snapshot_should_limit_freshness_and_stale_snapshot_should_not_match() {
        await using var server = new DnsServer(_ => new Reply());
        var networkCheckedAt = DateTimeOffset.UtcNow.AddMinutes(-29);
        var fresh = await server.Service.CheckAsync(HostName.Parse("191.217.251.72"), "191.217.251.72", null,
            networkCheckedAt: networkCheckedAt);
        var stale = await server.Service.CheckAsync(HostName.Parse("191.217.251.72"), "191.217.251.72", null,
            networkCheckedAt: DateTimeOffset.UtcNow.AddMinutes(-31));

        fresh.State.Should().Be(DomainValidationState.Valid);
        fresh.ValidUntil.Should().Be(networkCheckedAt.AddMinutes(30));
        fresh.MatchesNode.Should().BeTrue();
        (fresh with { ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1) }).MatchesNode.Should().BeNull();
        stale.State.Should().Be(DomainValidationState.NetworkStateStale);
        stale.MatchesNode.Should().BeNull();
        server.TotalQueries.Should().Be(0);
    }

    [Theory]
    [InlineData("191.217.251.72", "191.217.251.72", null)]
    [InlineData("2001:db8::1", null, "2001:db8::1")]
    [InlineData("::ffff:191.217.251.72", "191.217.251.72", null)]
    public async Task Literal_should_resolve_without_dns(string literal, string? ipv4, string? ipv6) {
        await using var server = new DnsServer(_ => new Reply());
        var result = await server.Service.CheckAsync(HostName.Parse(literal), ipv4, ipv6);

        result.State.Should().Be(DomainValidationState.Valid);
        result.MatchesNode.Should().BeTrue();
        result.IsComplete.Should().BeTrue();
        result.ResolvedAddresses.Should().Equal(ipv4 ?? ipv6!);
        server.TotalQueries.Should().Be(0);
    }

    [Fact]
    public async Task Canceled_caller_should_not_start_DNS_queries() {
        await using var server = new DnsServer(_ => new Reply());
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            server.Service.CheckAsync(Domain, "191.217.251.72", null, caller.Token));

        server.TotalQueries.Should().Be(0);
    }

    private static Record[] AddressAnswers(Question query, string? address, int ttl = 60) {
        return address is null
            ? []
            : [new Record(query.Name, query.Type, ttl, IPAddress.Parse(address).GetAddressBytes())];
    }

    private static Record Alias(string name, string target, int ttl) {
        return new Record(name, 5, ttl, EncodeName(target));
    }

    private static Record Soa(int ttl, int minimum) {
        var data = new List<byte>();
        data.AddRange(EncodeName("ns.example"));
        data.AddRange(EncodeName("admin.example"));
        foreach (var value in new[] { 1, 60, 60, 3600, minimum })
            AddNumber(data, value, 4);
        return new Record("example", 6, ttl, data.ToArray());
    }

    private static byte[] EncodeName(string name) {
        var result = new List<byte>();
        foreach (var label in name.Split('.')) {
            result.Add((byte)label.Length);
            result.AddRange(Encoding.ASCII.GetBytes(label));
        }

        result.Add(0);
        return result.ToArray();
    }

    private static void AddNumber(List<byte> bytes, int value, int length) {
        for (var shift = (length - 1) * 8; shift >= 0; shift -= 8)
            bytes.Add((byte)(value >> shift));
    }

    private sealed record Question(string Name, int Type);

    private sealed record Record(string Name, int Type, int Ttl, byte[] Data);

    private sealed record Reply(int Code = 0, Record[]? Answers = null, Record[]? Authorities = null);

    private sealed class DnsServer : IAsyncDisposable {
        private readonly ConcurrentDictionary<(string Name, int Type), int> _counts = new();
        private readonly Func<Question, Task<Reply>> _respond;
        private readonly Task _serve;
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));

        public DnsServer(Func<Question, Reply> respond) : this(query => Task.FromResult(respond(query))) { }

        public DnsServer(Func<Question, Task<Reply>> respond) {
            _respond = respond;
            var client = new LookupClient(new LookupClientOptions((IPEndPoint)_udp.Client.LocalEndPoint!) {
                UseCache = true, Retries = 0, ThrowDnsErrors = true, Timeout = TimeSpan.FromSeconds(2)
            });
            Service = new DomainValidationService(client, NullLogger<DomainValidationService>.Instance);
            _serve = ServeAsync();
        }

        public DomainValidationService Service { get; }

        public int TotalQueries {
            get => _counts.Values.Sum();
        }

        public async ValueTask DisposeAsync() {
            await _stop.CancelAsync();
            await _serve;
            _udp.Dispose();
            _stop.Dispose();
        }

        public int Count(string name, int type) {
            return _counts.GetValueOrDefault((name, type));
        }

        private async Task ServeAsync() {
            var pending = new List<Task>();
            try {
                while (true) {
                    try {
                        var received = await _udp.ReceiveAsync(_stop.Token);
                        pending.Add(RespondAsync(received));
                    }
                    catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionReset) {
                        // Windows reports ICMP from a canceled client's closed UDP port on the next receive.
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally {
                await Task.WhenAll(pending);
            }
        }

        private async Task RespondAsync(UdpReceiveResult received) {
            var query = received.Buffer;
            var offset = 12;
            var labels = new List<string>();
            while (query[offset] != 0) {
                var length = query[offset++];
                labels.Add(Encoding.ASCII.GetString(query, offset, length));
                offset += length;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(offset + 1));
            offset += 5;
            var question = new Question(string.Join('.', labels), type);
            _counts.AddOrUpdate((question.Name, question.Type), 1, (_, value) => value + 1);
            try {
                var reply = await _respond(question).WaitAsync(_stop.Token);
                var response = new List<byte>(query.AsSpan(0, offset).ToArray());
                response[2] = 0x81;
                response[3] = (byte)(0x80 | reply.Code);
                response[6] = response[8] = response[10] = response[11] = 0;
                response[7] = (byte)(reply.Answers?.Length ?? 0);
                response[9] = (byte)(reply.Authorities?.Length ?? 0);
                foreach (var record in (reply.Answers ?? []).Concat(reply.Authorities ?? [])) {
                    response.AddRange(EncodeName(record.Name));
                    AddNumber(response, record.Type, 2);
                    AddNumber(response, 1, 2);
                    AddNumber(response, record.Ttl, 4);
                    AddNumber(response, record.Data.Length, 2);
                    response.AddRange(record.Data);
                }

                await _udp.SendAsync(response.ToArray(), received.RemoteEndPoint, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
    }
}