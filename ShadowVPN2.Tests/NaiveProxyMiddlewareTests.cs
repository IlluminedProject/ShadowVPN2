using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Infrastructure.Middleware;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NaiveProxyMiddlewareTests {
    [Fact]
    public async Task Connect_forwards_authority_credentials_padding_and_binary_tunnel_after_upload_eof() {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var payload = Enumerable.Range(0, 40_000).Select(index => (byte)(index % 256)).ToArray();
        var greeting = new byte[] { 0, 0xff, 0x80, 1, 2, 3 };
        var headers = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = ServeAsync();
        await using var client = await H2Client.ConnectAsync(host.Address, "[2001:db8::1]:8443", "opaque-padding-123",
            "Basic dXNlcjpwYXNz", false, timeout.Token);
        var response = await client.ReadHeadersAsync(timeout.Token);
        response[":status"].Should().Be("200");
        response["padding"].Should().Be("backend-padding");
        await client.SendDataAsync(payload, true, timeout.Token);
        (await client.ReadBodyAsync(timeout.Token)).Should().Equal(greeting.Concat(payload));
        var forwarded = await headers.Task.WaitAsync(timeout.Token);
        forwarded.Should().StartWith("CONNECT [2001:db8::1]:8443 HTTP/1.1\r\n");
        forwarded.Should().Contain("Host: [2001:db8::1]:8443\r\n");
        forwarded.Should().Contain("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n");
        forwarded.Should().Contain("Padding: opaque-padding-123\r\n");
        await backend;

        async Task ServeAsync() {
            using var connection = await host.Backend.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            headers.SetResult(await ReadBackendHeadersAsync(stream, timeout.Token));
            var handshake =
                Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\nPadding: backend-padding\r\n\r\n");
            await stream.WriteAsync(handshake.Concat(greeting).ToArray(), timeout.Token);
            using var upload = new MemoryStream();
            await stream.CopyToAsync(upload, timeout.Token);
            upload.ToArray().Should().Equal(payload);
            // The backend responds after it observes the upload's half-close.
            await stream.WriteAsync(upload.GetBuffer().AsMemory(0, (int)upload.Length), timeout.Token);
            connection.Client.Shutdown(SocketShutdown.Send);
        }
    }

    [Theory]
    [InlineData(407)]
    [InlineData(400)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Backend_rejection_returns_error_status_without_entering_application_pipeline(int statusCode) {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var backend = ServeAsync();
        await using var client = await H2Client.ConnectAsync(host.Address, cancellationToken: timeout.Token);
        var response = await client.ReadHeadersAsync(timeout.Token);
        response[":status"].Should().Be("400");
        response.Should().NotContainKey("proxy-authenticate");
        response.Should().NotContainKey("padding");
        (await client.ReadBodyAsync(timeout.Token)).Should().BeEmpty();
        await backend;

        async Task ServeAsync() {
            using var connection = await host.Backend.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            await ReadBackendHeadersAsync(stream, timeout.Token);
            if (statusCode <= 0) {
                if (statusCode == -1)
                    connection.Client.LingerState = new LingerOption(true, 0);
                return;
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {statusCode} Rejected\r\nProxy-Authenticate: Basic realm=private\r\nPadding: secret\r\n\r\n"),
                timeout.Token);
        }
    }

    [Theory]
    [InlineData(null, "Basic dXNlcjpwYXNz")]
    [InlineData("padding", null)]
    [InlineData("padding", "Basic not-base64")]
    [InlineData("padding", "Basic dXNlcg==")]
    public async Task Missing_or_invalid_naive_headers_are_hidden_before_backend_connect(string? padding,
        string? credential) {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await H2Client.ConnectAsync(host.Address, padding: padding, credential: credential,
            cancellationToken: timeout.Token);
        var response = await client.ReadHeadersAsync(timeout.Token);
        response[":status"].Should().Be("418");
        response.Should().NotContainKey("proxy-authenticate");
        host.Backend.Pending().Should().BeFalse();
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com:0")]
    [InlineData("example.com:65536")]
    [InlineData("user@example.com:443")]
    [InlineData("example.com:443/path")]
    public async Task Invalid_authority_is_rejected_without_contacting_backend(string authority) {
        await using var host = await BridgeHost.StartAsync();
        var context = CreateContext(host, authority);
        await CreateMiddleware(host).InvokeAsync(context);
        context.Response.StatusCode.Should().Be(418);
        host.Backend.Pending().Should().BeFalse();
    }

    [Theory]
    [InlineData("Padding", "value\r\nInjected: header")]
    [InlineData("Padding", "value\nInjected: header")]
    [InlineData("Proxy-Authorization", "Basic dXNlcjpwYXNz\r\nInjected: header")]
    public async Task Header_injection_is_rejected_before_backend_connect(string header, string value) {
        await using var host = await BridgeHost.StartAsync();
        var context = CreateContext(host, "example.com:443");
        context.Request.Headers[header] = value;
        await CreateMiddleware(host).InvokeAsync(context);
        context.Response.StatusCode.Should().Be(418);
        host.Backend.Pending().Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_credentials_and_http1_connect_are_rejected_safely() {
        await using var host = await BridgeHost.StartAsync();
        var middleware = CreateMiddleware(host);
        var duplicate = CreateContext(host, "example.com:443");
        duplicate.Request.Headers["Proxy-Authorization"] = new[] { "Basic dXNlcjpwYXNz", "Basic dXNlcjpwYXNz" };
        await middleware.InvokeAsync(duplicate);
        duplicate.Response.StatusCode.Should().Be(418);
        var http1 = CreateContext(host, "example.com:443");
        http1.Request.Protocol = "HTTP/1.1";
        await middleware.InvokeAsync(http1);
        http1.Response.StatusCode.Should().Be(418);
        host.Backend.Pending().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_eof_or_client_abort_closes_backend_with_unfinished_upload(bool clientAbort) {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var backend = ServeAsync();
        await using var client =
            await H2Client.ConnectAsync(host.Address, endStream: false, cancellationToken: timeout.Token);
        (await client.ReadHeadersAsync(timeout.Token))[":status"].Should().Be("200");
        await client.SendDataAsync(new byte[] { 1 }, false, timeout.Token);
        if (clientAbort)
            await client.ResetAsync(timeout.Token);
        else
            (await client.ReadBodyAsync(timeout.Token)).Should().Equal(0x42);
        await backend;

        async Task ServeAsync() {
            using var connection = await host.Backend.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            await ReadBackendHeadersAsync(stream, timeout.Token);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nPadding: response\r\n\r\n"u8.ToArray(), timeout.Token);
            if (!clientAbort) {
                // Receive an upload byte before ending the response to avoid racing DATA against END_STREAM.
                var first = new byte[1];
                await stream.ReadExactlyAsync(first, timeout.Token);
                await stream.WriteAsync(new byte[] { 0x42 }, timeout.Token);
                connection.Client.Shutdown(SocketShutdown.Send);
            }

            var buffer = new byte[128];
            try {
                while (await stream.ReadAsync(buffer, timeout.Token) != 0) { }
            }
            catch (IOException exception) when (exception.InnerException is SocketException {
                                                    SocketErrorCode: SocketError.ConnectionReset
                                                    or SocketError.ConnectionAborted
                                                }) { }
        }
    }

    [Fact]
    public async Task Ordinary_requests_and_inactive_shared_listener_continue_to_next_middleware() {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var ordinary =
                     await H2Client.ConnectAsync(host.Address, method: "GET", cancellationToken: timeout.Token))
            (await ordinary.ReadHeadersAsync(timeout.Token))[":status"].Should().Be("418");
        var disabled = new SingBoxContext(new SingBoxConfig(), [], [], []);
        await host.Extension.ConfigureAsync(disabled, timeout.Token);
        await host.Extension.ApplyAsync(disabled, timeout.Token);
        await using var client = await H2Client.ConnectAsync(host.Address, cancellationToken: timeout.Token);
        (await client.ReadHeadersAsync(timeout.Token))[":status"].Should().Be("418");
        host.Backend.Pending().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connection_failure_or_oversized_handshake_returns_bad_gateway(bool oversized) {
        await using var host = await BridgeHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task backend;
        if (oversized)
            backend = ServeAsync();
        else {
            host.Backend.Stop();
            backend = Task.CompletedTask;
        }

        await using var client = await H2Client.ConnectAsync(host.Address, cancellationToken: timeout.Token);
        (await client.ReadHeadersAsync(timeout.Token))[":status"].Should().Be("502");
        await backend;

        async Task ServeAsync() {
            using var connection = await host.Backend.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            await ReadBackendHeadersAsync(stream, timeout.Token);
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nPadding: " + new string('x', 16 * 1024)), timeout.Token);
        }
    }

    private static NaiveProxyMiddleware CreateMiddleware(BridgeHost host) {
        return new NaiveProxyMiddleware(
            context => {
                context.Response.StatusCode = 418;
                return Task.CompletedTask;
            },
            host.Extension, NullLogger<NaiveProxyMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(BridgeHost host, string authority) {
        var context = new DefaultHttpContext();
        context.Request.Method = "CONNECT";
        context.Request.Scheme = "https";
        context.Request.Protocol = "HTTP/2";
        context.Request.Host = new HostString(authority);
        context.Connection.LocalPort = host.Address.Port;
        context.Request.Headers["Padding"] = "test-padding";
        context.Request.Headers["Proxy-Authorization"] = "Basic dXNlcjpwYXNz";
        return context;
    }

    private static async Task<string>
        ReadBackendHeadersAsync(NetworkStream stream, CancellationToken cancellationToken) {
        var bytes = new List<byte>();
        var singleByte = new byte[1];
        while (bytes.Count < 16 * 1024) {
            if (await stream.ReadAsync(singleByte, cancellationToken) == 0)
                throw new IOException("Backend handshake ended unexpectedly");
            bytes.Add(singleByte[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
        }

        throw new IOException("Backend handshake exceeded test limit");
    }

    // HttpClient emits :scheme and :path even for ordinary CONNECT. This single-stream test
    // client sends RFC 9113 CONNECT and disables dynamic HPACK indexing in server responses.
    private sealed class H2Client(TcpClient connection, SslStream stream) : IAsyncDisposable {
        private bool responseEnded;

        public async ValueTask DisposeAsync() {
            await stream.DisposeAsync();
            connection.Dispose();
        }

        public static async Task<H2Client> ConnectAsync(Uri address, string authority = "example.com:443",
            string? padding = "test-padding", string? credential = "Basic dXNlcjpwYXNz", bool endStream = true,
            CancellationToken cancellationToken = default, string method = "CONNECT") {
            var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, address.Port, cancellationToken);
            var stream = new SslStream(connection.GetStream(), false, (_, _, _, _) => true);
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = "localhost", ApplicationProtocols = [SslApplicationProtocol.Http2],
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken);
            var client = new H2Client(connection, stream);
            await stream.WriteAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray(), cancellationToken);
            // SETTINGS_HEADER_TABLE_SIZE = 0 avoids dynamic response header references.
            await client.WriteFrameAsync(4, 0, 0, new byte[] { 0, 1, 0, 0, 0, 0 }, cancellationToken);
            using var headers = new MemoryStream();
            WriteHeader(headers, ":method", method);
            WriteHeader(headers, ":authority", authority);
            if (method != "CONNECT") {
                WriteHeader(headers, ":scheme", "https");
                WriteHeader(headers, ":path", "/");
            }

            if (padding != null)
                WriteHeader(headers, "padding", padding);
            if (credential != null)
                WriteHeader(headers, "proxy-authorization", credential);
            await client.WriteFrameAsync(1, (byte)(4 | (endStream ? 1 : 0)), 1, headers.ToArray(), cancellationToken);
            return client;
        }

        public async Task<Dictionary<string, string>> ReadHeadersAsync(CancellationToken cancellationToken) {
            using var block = new MemoryStream();
            while (true) {
                var frame = await ReadFrameAsync(cancellationToken);
                if (frame.Type is not (1 or 9))
                    continue;
                if (frame.StreamId != 1)
                    throw new IOException("Unexpected response stream");
                responseEnded |= (frame.Flags & 1) != 0;
                block.Write(frame.Payload);
                if ((frame.Flags & 4) != 0)
                    return DecodeHeaders(block.ToArray());
            }
        }

        public async Task SendDataAsync(byte[] data, bool endStream, CancellationToken cancellationToken) {
            for (var offset = 0; offset < data.Length; offset += 16 * 1024) {
                var count = Math.Min(16 * 1024, data.Length - offset);
                await WriteFrameAsync(0, (byte)(endStream && offset + count == data.Length ? 1 : 0), 1,
                    data.AsMemory(offset, count), cancellationToken);
            }
        }

        public async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken) {
            using var body = new MemoryStream();
            while (!responseEnded) {
                var frame = await ReadFrameAsync(cancellationToken);
                if (frame.StreamId == 1 && frame.Type == 0) {
                    body.Write(frame.Payload);
                    responseEnded = (frame.Flags & 1) != 0;
                }
            }

            return body.ToArray();
        }

        public Task ResetAsync(CancellationToken cancellationToken) {
            return WriteFrameAsync(3, 0, 1, new byte[] { 0, 0, 0, 8 }, cancellationToken);
        }

        private async Task<(byte Type, byte Flags, int StreamId, byte[] Payload)> ReadFrameAsync(
            CancellationToken cancellationToken) {
            while (true) {
                var header = new byte[9];
                await stream.ReadExactlyAsync(header, cancellationToken);
                var length = (header[0] << 16) | (header[1] << 8) | header[2];
                if (length > 64 * 1024)
                    throw new IOException("Oversized test HTTP/2 frame");
                var payload = new byte[length];
                await stream.ReadExactlyAsync(payload, cancellationToken);
                var streamId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5)) & int.MaxValue;
                if (header[3] is 3 or 7)
                    throw new IOException($"HTTP/2 rejection frame {header[3]}: {Convert.ToHexString(payload)}");
                if (header[3] == 4 && (header[4] & 1) == 0) {
                    await WriteFrameAsync(4, 1, 0, ReadOnlyMemory<byte>.Empty, cancellationToken);
                    continue;
                }

                return (header[3], header[4], streamId, payload);
            }
        }

        private async Task WriteFrameAsync(byte type, byte flags, int streamId, ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) {
            var header = new byte[9];
            header[0] = (byte)(payload.Length >> 16);
            header[1] = (byte)(payload.Length >> 8);
            header[2] = (byte)payload.Length;
            header[3] = type;
            header[4] = flags;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5), streamId);
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
        }

        private static void WriteHeader(Stream stream, string name, string value) {
            stream.WriteByte(0);
            WriteString(name);
            WriteString(value);

            void WriteString(string text) {
                var bytes = Encoding.ASCII.GetBytes(text);
                if (bytes.Length >= 127)
                    throw new InvalidOperationException("Test header too long");
                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes);
            }
        }

        private static Dictionary<string, string> DecodeHeaders(byte[] block) {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var offset = 0;
            while (offset < block.Length) {
                var first = block[offset];
                if ((first & 0x80) != 0) {
                    var index = ReadInteger(7);
                    headers[":status"] = index switch {
                        8 => "200", 9 => "204", 10 => "206", 11 => "304", 12 => "400", 13 => "404", 14 => "500",
                        _ => throw new IOException($"Unexpected indexed HPACK header {index}")
                    };
                }
                else if ((first & 0xe0) == 0x20)
                    ReadInteger(5);
                else {
                    var index = ReadInteger((first & 0x40) != 0 ? 6 : 4);
                    var name = index == 0
                        ? ReadString()
                        : index switch {
                            >= 8 and <= 14 => ":status", 28 => "content-length", 31 => "content-type", 33 => "date",
                            48 => "proxy-authenticate", 54 => "server",
                            _ => throw new IOException($"Unexpected HPACK name index {index}")
                        };
                    headers[name] = ReadString();
                }
            }

            return headers;

            int ReadInteger(int prefix) {
                var mask = (1 << prefix) - 1;
                var value = block[offset++] & mask;
                if (value != mask)
                    return value;
                var shift = 0;
                byte next;
                do {
                    next = block[offset++];
                    value += (next & 127) << shift;
                    shift += 7;
                } while ((next & 128) != 0);

                return value;
            }

            string ReadString() {
                // Kestrel's HPACK encoder does not use Huffman encoding.
                if ((block[offset] & 128) != 0)
                    throw new IOException("Unexpected Huffman string in Kestrel response");
                var length = ReadInteger(7);
                var value = Encoding.ASCII.GetString(block, offset, length);
                offset += length;
                return value;
            }
        }
    }

    private sealed class BridgeHost(
        WebApplication application,
        X509Certificate2 certificate,
        Uri address,
        TcpListener backend,
        NaiveProxySharedPortExtension extension) : IAsyncDisposable {
        public Uri Address { get; } = address;
        public TcpListener Backend { get; } = backend;
        public NaiveProxySharedPortExtension Extension { get; } = extension;

        public async ValueTask DisposeAsync() {
            Backend.Stop();
            await application.StopAsync();
            await application.DisposeAsync();
            certificate.Dispose();
        }

        public static async Task<BridgeHost> StartAsync() {
            using var key = RSA.Create(2048);
            var request =
                new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated =
                request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => {
                listener.Protocols = HttpProtocols.Http2;
                listener.UseHttps(certificate);
            }));
            builder.Services.AddSingleton<NaiveProxySharedPortExtension>();
            var application = builder.Build();
            application.UseMiddleware<NaiveProxyMiddleware>();
            application.Run(context => {
                context.Response.StatusCode = 418;
                return Task.CompletedTask;
            });
            await application.StartAsync();
            var server = application.Services.GetRequiredService<IServer>();
            var address = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            var extension = application.Services.GetRequiredService<NaiveProxySharedPortExtension>();
            var inbound = new NaiveProxyInboundConfig {
                Tag = $"naive-{address.Port}", Listen = "0.0.0.0", ListenPort = address.Port,
                Users = [new NaiveProxyUser { Username = "test", Password = "test" }],
                Tls = new InboundTlsConfig {
                    Enabled = true,
                    Certificate = generated.ExportCertificatePem().Split('\n').ToList(),
                    Key = key.ExportPkcs8PrivateKeyPem().Split('\n').ToList()
                }
            };
            var config = new SingBoxConfig();
            config.Inbounds.Add(inbound);
            var context = new SingBoxContext(config, [], [], []);
            await extension.ConfigureAsync(context, CancellationToken.None);
            var backend = new TcpListener(IPAddress.Loopback, inbound.ListenPort!.Value);
            backend.Start();
            await extension.ApplyAsync(context, CancellationToken.None);
            return new BridgeHost(application, certificate, address, backend, extension);
        }
    }
}