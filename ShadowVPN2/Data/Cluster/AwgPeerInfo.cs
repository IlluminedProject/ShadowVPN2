using System.Net;

namespace ShadowVPN2.Data.Cluster;

public record AwgPeerInfo(string PublicKey, string MeshIp, string PublicAddress) {
    public string PublicHost {
        get {
            if (Uri.TryCreate(PublicAddress, UriKind.Absolute, out var absoluteUri) &&
                !string.IsNullOrEmpty(absoluteUri.Host))
                return absoluteUri.Host;

            if (IPAddress.TryParse(PublicAddress, out _))
                return PublicAddress;

            var separator = PublicAddress.LastIndexOf(':');
            return separator > 0 && PublicAddress.IndexOf(':') == separator
                ? PublicAddress[..separator]
                : PublicAddress;
        }
    }
}