using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ShadowVPN2.Data.SingBox;

public sealed class SingBoxRuntimeEndpoint {
    public SingBoxRuntimeEndpoint() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Address = $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    public string Address { get; }
    public string Secret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}