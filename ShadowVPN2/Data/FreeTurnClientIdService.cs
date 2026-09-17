using System.Security.Cryptography;
using System.Text;

namespace ShadowVPN2.Data;

public sealed class FreeTurnClientIdService {
    public string GetClientId(Guid subscriptionId) {
        var input = Encoding.UTF8.GetBytes($"shadowvpn:freeturn:v1:{subscriptionId:D}");
        return Convert.ToHexString(SHA256.HashData(input)[..16]).ToLowerInvariant();
    }
}