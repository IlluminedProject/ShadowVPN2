using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Data.SingBox;
using Xunit;

namespace ShadowVPN2.Tests;

// lx: runtime-management — end-to-end check of the REST seam against a local
// stub that mimics the sing-box runtime routes (SPEC 019/152).
public class SingBoxRuntimeClientTests : IDisposable {
    private readonly HttpListener _listener = new();
    private readonly string _secret = "test-secret";

    public SingBoxRuntimeClientTests() {
        var port = 51987 + Random.Shared.Next(100);
        while (true) {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try {
                _listener.Start();
                break;
            }
            catch (HttpListenerException) {
                port++;
            }
        }

        Port = port;
        _ = Task.Run(AcceptLoopAsync);
    }

    private int Port { get; }

    public void Dispose() {
        _listener.Stop();
        _listener.Close();
    }

    private async Task AcceptLoopAsync() {
        while (_listener.IsListening) {
            try {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                await HandleAsync(context).ConfigureAwait(false);
            }
            catch (Exception) when (!_listener.IsListening) {
                return;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context) {
        try {
            var auth = context.Request.Headers["Authorization"];
            if (auth != $"Bearer {_secret}") {
                context.Response.StatusCode = 401;
            }
            else if (context.Request.HttpMethod == "PUT" && context.Request.Url!.AbsolutePath.EndsWith("/users")) {
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body))
                    .ConfigureAwait(false);
            }
            else {
                context.Response.StatusCode = 404;
            }
        }
        finally {
            context.Response.Close();
        }
    }

    [Fact]
    public async Task ReplaceInboundUsers_SendsAuthAndPayload() {
        using var client = new SingBoxRuntimeClient($"http://127.0.0.1:{Port}", _secret,
            NullLogger<SingBoxRuntimeClient>.Instance);
        var result = await client.ReplaceInboundUsersAsync("hy2-4443",
            [new RuntimeUserDto { Username = "alice", Password = "secret" }]);

        var user = Assert.Single(result);
        Assert.Equal("alice", user.Username);
    }

    [Fact]
    public async Task WrongSecret_Throws() {
        using var client = new SingBoxRuntimeClient($"http://127.0.0.1:{Port}", "wrong",
            NullLogger<SingBoxRuntimeClient>.Instance);
        await Assert.ThrowsAsync<SingBoxRuntimeClient.SingBoxRuntimeException>(() =>
            client.ReplaceInboundUsersAsync("hy2-4443", [new RuntimeUserDto { Username = "a" }]));
    }
}