using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Frontend.Components;
using Xunit;

namespace ShadowVPN2.Frontend.Tests;

public sealed class DomainDnsDetailsTests {
    [Theory]
    [InlineData(DomainValidationState.NameNotFound, false, "Domain name not found", "No")]
    [InlineData(DomainValidationState.NetworkStateStale, false, "Node network information is stale", "Unknown")]
    [InlineData(DomainValidationState.Valid, true, "Expired: Domain points to this node", "Unknown")]
    public async Task Render_displays_state_and_unknown_matching_when_result_is_unusable(
        DomainValidationState state, bool expired, string expectedStatus, string expectedMatch) {
        var now = DateTimeOffset.UtcNow;
        var status = new DomainCheckResponse {
            State = state,
            Domain = HostName.Parse("vpn.example.com"),
            ResolvedAddresses = [],
            CheckedAt = now.AddMinutes(-1),
            ValidUntil = expired ? now.AddSeconds(-1) : now.AddMinutes(1)
        };

        var html = await RenderAsync(status);

        Assert.Contains(expectedStatus, html);
        Assert.Matches($@"<dt>Matches node</dt>\s*<dd>{expectedMatch}</dd>", html);
        Assert.Contains(expired ? "<dd>Expired</dd>" : "<dd>Fresh</dd>", html);
    }

    [Fact]
    public async Task Render_without_domain_displays_not_applicable_instead_of_expired_or_incomplete() {
        var status = new DomainCheckResponse {
            State = DomainValidationState.NoDomain,
            Domain = null,
            ResolvedAddresses = [],
            CheckedAt = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.MinValue
        };

        var html = await RenderAsync(status);

        Assert.Contains("No domain configured", html);
        Assert.Contains("Not applicable", html);
        Assert.DoesNotContain("Expired", html);
        Assert.DoesNotContain("Incomplete", html);
    }

    [Fact]
    public async Task Render_displays_both_dns_families_and_observation_context_and_encodes_errors() {
        var now = DateTimeOffset.UtcNow;
        var observer = Guid.NewGuid();
        var status = new DomainCheckResponse {
            State = DomainValidationState.LookupFailed,
            Domain = HostName.Parse("vpn.example.com"),
            ResolvedAddresses = ["203.0.113.42"],
            CheckedAt = now,
            ValidUntil = now.AddMinutes(1),
            ExpectedIpv4 = "203.0.113.42",
            ExpectedIpv6 = "2001:db8::42",
            NetworkCheckedAt = now.AddSeconds(-30),
            ObserverNodeId = observer,
            A = new DnsFamilyResult {
                Addresses = ["203.0.113.42"],
                CanonicalNames = ["canonical.example.com"],
                ValidUntil = now.AddMinutes(1),
                NameNotFound = false
            },
            Aaaa = new DnsFamilyResult {
                Addresses = [],
                CanonicalNames = [],
                ValidUntil = now.AddMinutes(1),
                Error = "<script>lookup failed</script>",
                NameNotFound = false
            }
        };

        var html = await RenderAsync(status);

        Assert.Contains("203.0.113.42", html);
        Assert.Contains("2001:db8::42", html);
        Assert.Contains("canonical.example.com", html);
        Assert.Contains("A records", html);
        Assert.Contains("AAAA records", html);
        Assert.Contains(observer.ToString(), html);
        Assert.Contains(status.CheckedAt.ToString("u"), html);
        Assert.Contains(status.NetworkCheckedAt.Value.ToString("u"), html);
        Assert.Contains("&lt;script&gt;lookup failed&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    private static async Task<string> RenderAsync(DomainCheckResponse status) {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => {
            var rendered = await renderer.RenderComponentAsync<DomainDnsDetails>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                    { [nameof(DomainDnsDetails.Status)] = status }));
            return rendered.ToHtmlString();
        });
    }
}