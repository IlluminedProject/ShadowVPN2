using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class UpdaterImageClient(IOptions<UpdaterOptions> options) {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ContainerImageInfo> GetAsync(CancellationToken cancellationToken) {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.Value.SocketPath), cancellationToken);
        await using var stream = new NetworkStream(socket, false);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync("{\"operation\":\"info\"}".AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken) ??
                   throw new IOException("The updater closed the connection");
        var response = JsonSerializer.Deserialize<ImageInfoResponse>(line, JsonOptions);
        return response is { Status: "Succeeded", ImageInfo: { } image }
            ? image
            : throw new IOException("The updater could not inspect the running application image");
    }

    private sealed class ImageInfoResponse {
        public string? Status { get; set; }
        public ContainerImageInfo? ImageInfo { get; set; }
    }
}