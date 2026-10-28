using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Controllers;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Renderers;
using ShadowVPN2.Data.Subscription.Services;
using ShadowVPN2.Frontend;
using ShadowVPN2.Infrastructure.Middleware;
using Xunit;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;
using SubscriptionEndpoint = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionEndpoint;
using ProtocolSubscription = ShadowVPN2.Data.Subscription.Models.Common.ProtocolSubscription;
using Hysteria2ConnectionInfo = ShadowVPN2.Data.Subscription.Models.Connections.Hysteria2ConnectionInfo;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionHttpTests {
    [Fact]
    public async Task Shared_url_negotiates_content_and_explicit_format_overrides_browser_headers() {
        await using var host = await SubscriptionHost.StartAsync();
        var path = $"/s/{SubscriptionHost.Id}";
        using var browser = new HttpRequestMessage(HttpMethod.Get, path);
        browser.Headers.Accept.ParseAdd("text/html");
        using var page = await host.Client.SendAsync(browser);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("subscription-browser-endpoint", await page.Content.ReadAsStringAsync());

        using var yamlRequest = new HttpRequestMessage(HttpMethod.Get, path);
        yamlRequest.Headers.UserAgent.ParseAdd("FlClash/1.0");
        using var yaml = await host.Client.SendAsync(yamlRequest);
        Assert.Equal(HttpStatusCode.OK, yaml.StatusCode);
        Assert.Equal("text/yaml", yaml.Content.Headers.ContentType!.MediaType);
        Assert.Contains("type: hysteria2", await yaml.Content.ReadAsStringAsync());
        Assert.True(yaml.Headers.CacheControl!.NoStore);

        using var fallback = await host.Client.GetAsync(path);
        var links = Encoding.UTF8.GetString(Convert.FromBase64String(await fallback.Content.ReadAsStringAsync()));
        Assert.StartsWith("hysteria2://", links);
        Assert.Equal("device", Encoding.UTF8.GetString(Convert.FromBase64String(
            Assert.Single(fallback.Headers.GetValues("profile-title"))["base64:".Length..])));
        Assert.Equal(new Uri(host.Client.BaseAddress!, path).AbsoluteUri,
            Assert.Single(fallback.Headers.GetValues("profile-web-page-url")));
        using var explicitRequest = new HttpRequestMessage(HttpMethod.Get, path + "/mihomo-provider");
        explicitRequest.Headers.Accept.ParseAdd("text/html");
        using var explicitResponse = await host.Client.SendAsync(explicitRequest);
        var provider = await explicitResponse.Content.ReadAsStringAsync();
        Assert.Contains("type: hysteria2", provider);
        Assert.DoesNotContain("proxy-groups:", provider);
        using var head = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path + "/base64"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("", await head.Content.ReadAsStringAsync());
        Assert.True(head.Content.Headers.ContentLength > 0);
        Assert.Equal(Assert.Single(fallback.Headers.GetValues("profile-title")),
            Assert.Single(head.Headers.GetValues("profile-title")));
        Assert.Equal(Assert.Single(fallback.Headers.GetValues("profile-web-page-url")),
            Assert.Single(head.Headers.GetValues("profile-web-page-url")));
    }

    [Fact]
    public async Task Unknown_formats_and_missing_subscriptions_return_not_found() {
        await using var host = await SubscriptionHost.StartAsync();
        var path = $"/s/{SubscriptionHost.Id}";
        using var unsupported = await host.Client.GetAsync(path + "/unknown-format");
        Assert.Equal(HttpStatusCode.NotFound, unsupported.StatusCode);
        using var missing = await host.Client.GetAsync($"/s/{Guid.NewGuid()}/base64");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Internal_json_remains_available_with_protocol_properties() {
        await using var host = await SubscriptionHost.StartAsync();
        using var response = await host.Client.GetAsync($"/api/Subscription/{SubscriptionHost.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("device", json.RootElement.GetProperty("clientName").GetString());
        var endpoint = json.RootElement.GetProperty("protocols")[0].GetProperty("endpoints")[0];
        Assert.Equal("hysteria2", endpoint.GetProperty("connection").GetProperty("type").GetString());
        Assert.Equal("password", endpoint.GetProperty("connection").GetProperty("password").GetString());
        Assert.StartsWith("hysteria2://", endpoint.GetProperty("shareUrl").GetString());
    }

    [Theory]
    [InlineData("singbox", "application/json", "hysteria2")]
    [InlineData("json", "application/json", "hysteria")]
    [InlineData("v2ray-json", "application/json", "hysteria")]
    [InlineData("stash", "text/yaml", "hysteria2")]
    public async Task Explicit_formats_export_protocol_credentials(string format, string mime, string protocol) {
        await using var host = await SubscriptionHost.StartAsync();
        using var response = await host.Client.GetAsync($"/s/{SubscriptionHost.Id}/{format}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mime, response.Content.Headers.ContentType!.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(protocol, content);
        Assert.Contains("password", content);
    }

    [Fact]
    public async Task Legacy_clash_reports_incompatible_protocol_without_html_error_page() {
        await using var host = await SubscriptionHost.StartAsync();
        using var response = await host.Client.GetAsync($"/s/{SubscriptionHost.Id}/clash");
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("<html", await response.Content.ReadAsStringAsync());
    }

    private sealed class SubscriptionHost(WebApplication app, HttpClient client)
        : IAsyncDisposable {
        public static readonly Guid Id = Guid.Parse("b8bba7a4-88e2-4af5-a13b-c40f634eb431");
        public HttpClient Client { get; } = client;

        public static async Task<SubscriptionHost> StartAsync() {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(SubscriptionRendererRegistry.Default);
            builder.Services.AddSingleton(SubscriptionProtocolExporterRegistry.Default);
            builder.Services.AddSingleton<ISubscriptionSource, SubscriptionSource>();
            builder.Services.AddScoped<ISubscriptionApplicationService, SubscriptionApplicationService>();
            builder.Services.AddControllers().AddApplicationPart(typeof(SubscriptionController).Assembly);
            var app = builder.Build();
            app.UseStatusCodePagesWithReExecute("/browser-error");
            app.UseMiddleware<SubscriptionResponseMiddleware>();
            app.MapControllers();
            app.MapGet("/s/{id:guid}", () => Microsoft.AspNetCore.Http.Results.Content(
                "<html>subscription-browser-endpoint</html>", "text/html"));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            return new SubscriptionHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync() {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    private sealed class SubscriptionSource : ISubscriptionSource {
        public Task<SubscriptionResponse?> GetSubscriptionAsync(Guid subscriptionId) => Task.FromResult(
            subscriptionId != SubscriptionHost.Id ? null : new SubscriptionResponse {
                ClientName = "device",
                Protocols = [new ProtocolSubscription {
                    Protocol = "Hysteria2",
                    Endpoints = [new SubscriptionEndpoint {
                        Name = "Main", Address = HostAddress.Parse("vpn.example.com"),
                        Connection = new Hysteria2ConnectionInfo {
                            ServerAddress = HostName.Parse("vpn.example.com"), PortBinding = new PortBinding(443),
                            Password = "password"
                        }
                    }]
                }]
            });
    }
}
