using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using DnsClient;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DomainValidationServiceTests {
    private readonly DomainValidationService _service = new(new LookupClient(),
        NullLogger<DomainValidationService>.Instance);

    [Fact]
    public async Task Check_should_report_missing_domain() {
        var result = await _service.CheckAsync(null, null, null);

        result.State.Should().Be(DomainValidationState.NoDomain);
        result.ResolvedAddresses.Should().BeEmpty();
    }

    [Theory]
    [InlineData("191.217.251.72", null, 0, DomainValidationState.Valid)]
    [InlineData("191.217.251.72", "2001:db8::1", 0, DomainValidationState.Valid)]
    [InlineData("191.217.251.72", "2001:db8::2", 0, DomainValidationState.PartialMatch)]
    [InlineData("203.0.113.1", null, 0, DomainValidationState.Mismatch)]
    [InlineData(null, null, 0, DomainValidationState.NoRecords)]
    [InlineData(null, null, 3, DomainValidationState.NoRecords)]
    [InlineData("191.217.251.72", null, 2, DomainValidationState.LookupFailed)]
    public async Task Check_should_use_DNS_records_for_local_hostname(string? ipv4, string? ipv6,
        int responseCode, DomainValidationState expectedState) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var client = new LookupClient(new LookupClientOptions((IPEndPoint)server.Client.LocalEndPoint!) {
            UseCache = false,
            Retries = 0,
            ThrowDnsErrors = true
        });
        var service = new DomainValidationService(client, NullLogger<DomainValidationService>.Instance);
        var serve = RespondAsync();

        // OS resolution treats the local hostname specially; DNS must only return the published records.
        var result = await service.CheckAsync(HostName.Parse(Environment.MachineName), "191.217.251.72",
            "2001:db8::1", timeout.Token);
        await serve;

        result.State.Should().Be(expectedState);
        if (responseCode == 0)
            result.ResolvedAddresses.Should().BeEquivalentTo(new[] { ipv4, ipv6 }.OfType<string>());
        else
            result.ResolvedAddresses.Should().BeEmpty();

        async Task RespondAsync() {
            for (var request = 0; request < 2; request++) {
                var received = await server.ReceiveAsync(timeout.Token);
                var query = received.Buffer;
                var questionEnd = 12;
                while (query[questionEnd] != 0) questionEnd += query[questionEnd] + 1;
                var type = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(questionEnd + 1));
                questionEnd += 5;
                var address = type == 1 ? ipv4 : ipv6;
                var hasAnswer = responseCode == 0 && address != null;
                var response = new List<byte>(query.AsSpan(0, questionEnd).ToArray());
                response[2] = 0x81;
                response[3] = (byte)(0x80 | responseCode);
                response[6] = 0;
                response[7] = hasAnswer ? (byte)1 : (byte)0;
                response[8] = response[9] = response[10] = response[11] = 0;
                if (hasAnswer) {
                    var bytes = IPAddress.Parse(address!).GetAddressBytes();
                    response.AddRange([0xc0, 0x0c, 0, (byte)type, 0, 1, 0, 0, 0, 60, 0, (byte)bytes.Length]);
                    response.AddRange(bytes);
                }

                await server.SendAsync(response.ToArray(), received.RemoteEndPoint, timeout.Token);
            }
        }
    }
}