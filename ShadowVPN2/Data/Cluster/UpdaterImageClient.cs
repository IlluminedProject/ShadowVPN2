using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class UpdaterImageClient(IOptions<UpdaterOptions> options) {
    public async Task<ContainerImageInfo> GetAsync(CancellationToken cancellationToken) {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.Value.SocketPath), cancellationToken);
        await using var stream = new NetworkStream(socket, false);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var request = JsonSerializer.Serialize(new UpdaterRequest { Operation = UpdaterOperation.Info }, UpdaterJsonContext.Default.UpdaterRequest);
        await writer.WriteLineAsync(request.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken) ?? throw new IOException("The updater closed the connection");
        var response = JsonSerializer.Deserialize(line, UpdaterJsonContext.Default.UpdaterResponse);
        return response is { Status: UpdaterStatus.Succeeded, ImageInfo: { } image }
            ? image
            : throw new IOException("The updater could not inspect the running application image");
    }
}
