using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using ShadowVPN2.Data.SingBox;

namespace ShadowVPN2.Infrastructure.Middleware;

public sealed class NaiveProxyMiddleware(
    RequestDelegate next,
    NaiveProxySharedPortExtension sharedPortExtension,
    ILogger<NaiveProxyMiddleware> logger) {
    private const int MaximumHeaderBytes = 16 * 1024;
    private const int TransferBufferSize = 16 * 1024;

    public async Task InvokeAsync(HttpContext context) {
        if (!HttpMethods.IsConnect(context.Request.Method) || !context.Request.IsHttps ||
            !sharedPortExtension.TryGetBackend(context.Connection.LocalPort, out var backendPort)) {
            await next(context);
            return;
        }

        if (context.Request.Protocol != "HTTP/2" ||
            !TryGetHeader(context.Request.Headers, "Padding", out var padding) ||
            !TryGetHeader(context.Request.Headers, "Proxy-Authorization", out var authorization) ||
            !IsBasicAuthorization(authorization) ||
            !IsValidAuthority(context.Request.Host.Value)) {
            await next(context);
            return;
        }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        await using var abortRegistration = cancellation.Token.Register(socket.Dispose);
        var headerBuffer = ArrayPool<byte>.Shared.Rent(MaximumHeaderBytes);
        try {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(IPAddress.Loopback, backendPort, handshake.Token);
            await using var stream = new NetworkStream(socket, false);
            var authority = context.Request.Host.Value!;
            var requestLength = 0;
            AppendRequestPart("CONNECT ");
            AppendRequestPart(authority);
            AppendRequestPart(" HTTP/1.1\r\nHost: ");
            AppendRequestPart(authority);
            AppendRequestPart("\r\nProxy-Authorization: ");
            AppendRequestPart(authorization);
            AppendRequestPart("\r\nPadding: ");
            AppendRequestPart(padding);
            AppendRequestPart("\r\n\r\n");

            void AppendRequestPart(string value) {
                requestLength += Encoding.ASCII.GetBytes(value, headerBuffer.AsSpan(requestLength));
            }

            BackendResponse response;
            try {
                await stream.WriteAsync(headerBuffer.AsMemory(0, requestLength), handshake.Token);
                response = await ReadResponseAsync(stream, headerBuffer, handshake.Token);
            }
            catch (IOException exception) when (exception.InnerException is SocketException {
                                                    SocketErrorCode: SocketError.ConnectionReset
                                                    or SocketError.ConnectionAborted
                                                }) {
                logger.LogWarning(exception, "NaiveProxy backend reset CONNECT handshake on port {BackendPort}",
                    backendPort);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            handshake.CancelAfter(Timeout.InfiniteTimeSpan);

            if (response.StatusCode is StatusCodes.Status407ProxyAuthenticationRequired
                or StatusCodes.Status400BadRequest) {
                if (response.BytesRead == 0)
                    logger.LogWarning("NaiveProxy backend closed CONNECT handshake on port {BackendPort}", backendPort);
                else {
                    logger.LogWarning(
                        "NaiveProxy backend rejected CONNECT with status {StatusCode} on port {BackendPort}",
                        response.StatusCode, backendPort);
                }

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (response.StatusCode != StatusCodes.Status200OK)
                throw new IOException("NaiveProxy backend rejected CONNECT");

            var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = null;
            var bodyRate = context.Features.Get<IHttpMinRequestBodyDataRateFeature>();
            if (bodyRate != null) bodyRate.MinDataRate = null;

            context.Response.StatusCode = StatusCodes.Status200OK;
            if (response.Padding != null)
                context.Response.Headers["Padding"] = response.Padding;
            await context.Response.StartAsync(cancellation.Token);
            await context.Response.BodyWriter.FlushAsync(cancellation.Token);
            await TransferAsync(context, socket, stream,
                headerBuffer.AsMemory(response.HeaderLength, response.BytesRead - response.HeaderLength), cancellation);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException
                                              or ObjectDisposedException) {
            if (context.RequestAborted.IsCancellationRequested)
                return;
            logger.LogWarning(exception, "NaiveProxy backend bridge failed on port {BackendPort}", backendPort);
            if (context.Response.HasStarted)
                context.Abort();
            else
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
        finally {
            await cancellation.CancelAsync();
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    private static async Task TransferAsync(HttpContext context, Socket socket, NetworkStream stream,
        ReadOnlyMemory<byte> readAhead, CancellationTokenSource cancellation) {
        var upload = UploadAsync(context, socket, stream, cancellation.Token);
        var download = DownloadAsync(context, socket, readAhead, cancellation.Token);
        try {
            var first = await Task.WhenAny(upload, download);
            await first;
            if (first == upload) {
                // EOF from the client closes only the backend's receive direction.
                await download;
            }
        }
        finally {
            // Backend EOF, either pump failing, or a client abort must unblock both pumps.
            await cancellation.CancelAsync();
            socket.Dispose();
            await ObserveCompletionAsync(upload);
            await ObserveCompletionAsync(download);
        }
    }

    private static async Task UploadAsync(HttpContext context, Socket socket, NetworkStream stream,
        CancellationToken cancellationToken) {
        await context.Request.BodyReader.CopyToAsync(stream, cancellationToken);
        socket.Shutdown(SocketShutdown.Send);
    }

    private static async Task DownloadAsync(HttpContext context, Socket socket, ReadOnlyMemory<byte> readAhead,
        CancellationToken cancellationToken) {
        var writer = context.Response.BodyWriter;
        if (!readAhead.IsEmpty) {
            var result = await writer.WriteAsync(readAhead, cancellationToken);
            if (result.IsCanceled)
                throw new OperationCanceledException(cancellationToken);
            if (result.IsCompleted)
                return;
        }

        while (true) {
            var received = await socket.ReceiveAsync(writer.GetMemory(TransferBufferSize)[..TransferBufferSize],
                SocketFlags.None, cancellationToken);
            if (received == 0)
                return;
            writer.Advance(received);
            var result = await writer.FlushAsync(cancellationToken);
            if (result.IsCanceled)
                throw new OperationCanceledException(cancellationToken);
            if (result.IsCompleted)
                return;
        }
    }

    private static async Task ObserveCompletionAsync(Task task) {
        try {
            await task;
        }
        catch (Exception) {
            // The first pump's error propagates; observe errors caused by closing the other pump.
        }
    }

    private static async Task<BackendResponse> ReadResponseAsync(NetworkStream stream, byte[] buffer,
        CancellationToken cancellationToken) {
        var bytesRead = 0;
        while (bytesRead < MaximumHeaderBytes) {
            var previousLength = bytesRead;
            var received = await stream.ReadAsync(buffer.AsMemory(bytesRead, MaximumHeaderBytes - bytesRead),
                cancellationToken);
            if (received == 0)
                return new BackendResponse(StatusCodes.Status400BadRequest, null, 0, 0);
            bytesRead += received;
            var delimiter = buffer.AsSpan(Math.Max(0, previousLength - 3), bytesRead - Math.Max(0, previousLength - 3))
                .IndexOf("\r\n\r\n"u8);
            if (delimiter >= 0) {
                var headerLength = Math.Max(0, previousLength - 3) + delimiter + 4;
                return ParseResponse(buffer.AsSpan(0, headerLength), headerLength, bytesRead);
            }
        }

        throw new IOException("NaiveProxy backend CONNECT headers exceeded the limit");
    }

    private static BackendResponse ParseResponse(ReadOnlySpan<byte> headers, int headerLength, int bytesRead) {
        var lineEnd = headers.IndexOf("\r\n"u8);
        var status = headers[..lineEnd];
        if (status.Length < 12 || (!status.StartsWith("HTTP/1.1 "u8) && !status.StartsWith("HTTP/1.0 "u8)) ||
            (status.Length > 12 && status[12] != ' ') ||
            !int.TryParse(status.Slice(9, 3), NumberStyles.None,
                CultureInfo.InvariantCulture, out var statusCode))
            throw new IOException("Invalid NaiveProxy backend CONNECT status");

        string? padding = null;
        headers = headers[(lineEnd + 2)..];
        while ((lineEnd = headers.IndexOf("\r\n"u8)) > 0) {
            var line = headers[..lineEnd];
            var colon = line.IndexOf((byte)':');
            if (colon <= 0)
                throw new IOException("Invalid NaiveProxy backend CONNECT header");
            if (IsPaddingHeader(line[..colon])) {
                var value = Encoding.ASCII.GetString(line[(colon + 1)..]).Trim(' ', '\t');
                if (padding != null || !IsSafeHeader(value))
                    throw new IOException("Invalid NaiveProxy backend padding header");
                padding = value;
            }

            headers = headers[(lineEnd + 2)..];
        }

        return new BackendResponse(statusCode, padding, headerLength, bytesRead);
    }

    private static bool IsPaddingHeader(ReadOnlySpan<byte> name) {
        var expected = "padding"u8;
        if (name.Length != expected.Length)
            return false;
        for (var index = 0; index < name.Length; index++) {
            if ((name[index] | 0x20) != expected[index])
                return false;
        }

        return true;
    }

    private static bool TryGetHeader(IHeaderDictionary headers, string name, out string value) {
        var values = headers[name];
        value = values.Count == 1 ? values[0] ?? "" : "";
        return IsSafeHeader(value);
    }

    private static bool IsSafeHeader(string value) {
        if (value.Length is 0 or > 4096)
            return false;
        foreach (var character in value) {
            if (character is < ' ' or > '~')
                return false;
        }

        return true;
    }

    private static bool IsBasicAuthorization(string value) {
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        var token = value.AsSpan(6);
        foreach (var character in token) {
            if (!(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '='))
                return false;
        }

        Span<byte> decoded = stackalloc byte[3072];
        return Convert.TryFromBase64Chars(token, decoded, out var length) &&
               decoded[..length].IndexOf((byte)':') > 0;
    }

    private static bool IsValidAuthority(string? authority) {
        if (authority is null || authority.Length is 0 or > 512)
            return false;
        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(authority.AsSpan(colon + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            return false;
        var host = authority.AsSpan(0, colon);
        if (host[0] == '[') {
            if (host.Length < 3 || host[^1] != ']' || host.Contains('%'))
                return false;
            return IPAddress.TryParse(host[1..^1], out var address) &&
                   address.AddressFamily == AddressFamily.InterNetworkV6;
        }

        if (host.Length > 253)
            return false;
        if (host[^1] == '.')
            host = host[..^1];
        while (!host.IsEmpty) {
            var dot = host.IndexOf('.');
            var label = dot < 0 ? host : host[..dot];
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
                return false;
            foreach (var character in label) {
                if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
                    return false;
            }

            if (dot < 0)
                return true;
            host = host[(dot + 1)..];
            if (host.IsEmpty)
                return false;
        }

        return false;
    }

    private readonly record struct BackendResponse(int StatusCode, string? Padding, int HeaderLength, int BytesRead);
}