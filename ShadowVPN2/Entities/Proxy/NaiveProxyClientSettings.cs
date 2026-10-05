using System.Security.Cryptography;

namespace ShadowVPN2.Entities.Proxy;

public sealed class NaiveProxyClientSettings {
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public static NaiveProxyClientSettings Create() {
        return new NaiveProxyClientSettings {
            Username = Guid.NewGuid().ToString("N"),
            Password = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
                32)
        };
    }
}