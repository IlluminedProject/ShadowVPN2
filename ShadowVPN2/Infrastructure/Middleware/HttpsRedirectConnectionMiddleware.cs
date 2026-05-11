using System.Buffers;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace ShadowVPN2.Infrastructure.Middleware;

public class HttpsRedirectConnectionMiddleware(ConnectionDelegate next)
{
    public async Task OnConnectionAsync(ConnectionContext context)
    {
        var input = context.Transport.Input;
        var result = await input.ReadAsync();
        var buffer = result.Buffer;

        if (buffer.Length > 0)
        {
            var firstByte = buffer.First.Span[0];
            input.AdvanceTo(buffer.Start);

            if (firstByte != 0x16)
            {
                await SendHttpsRedirect(context);
                return;
            }
        }
        else
        {
            input.AdvanceTo(buffer.Start);
        }

        await next(context);
    }

    private static async Task SendHttpsRedirect(ConnectionContext context)
    {
        var input = context.Transport.Input;

        // Read the full HTTP request line to extract Host header
        string? host = null;
        var path = "/";

        while (true)
        {
            var result = await input.ReadAsync();
            var buffer = result.Buffer;

            if (TryParseHttpRequest(buffer, out host, out path))
            {
                input.AdvanceTo(buffer.End);
                break;
            }

            if (result.IsCompleted)
            {
                input.AdvanceTo(buffer.End);
                break;
            }

            input.AdvanceTo(buffer.Start, buffer.End);
        }

        host ??= context.LocalEndPoint is IPEndPoint ep ? $"{ep.Address}:{ep.Port}" : "localhost";

        var location = $"https://{host}{path}";
        var body = $"<html><body>Redirecting to <a href=\"{location}\">{location}</a></body></html>";
        var response =
            $"HTTP/1.1 301 Moved Permanently\r\nLocation: {location}\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        var output = context.Transport.Output;
        await output.WriteAsync(Encoding.UTF8.GetBytes(response));
        await output.CompleteAsync();
    }

    private static bool TryParseHttpRequest(ReadOnlySequence<byte> buffer, out string? host, out string path)
    {
        host = null;
        path = "/";

        var text = Encoding.ASCII.GetString(buffer);
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            return false;

        var lines = text[..headerEnd].Split("\r\n");
        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ');
            if (parts.Length >= 2)
                path = parts[1];
        }

        foreach (var line in lines.Skip(1))
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                host = line[5..].Trim();
                break;
            }

        return true;
    }
}