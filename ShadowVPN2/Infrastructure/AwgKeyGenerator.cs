using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace ShadowVPN2.Infrastructure;

public static class AwgKeyGenerator
{
    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privateKey = ((X25519PrivateKeyParameters)keyPair.Private).GetEncoded();
        var publicKey = ((X25519PublicKeyParameters)keyPair.Public).GetEncoded();

        return (Convert.ToBase64String(privateKey), Convert.ToBase64String(publicKey));
    }

    public static string GetPublicKey(string privateKeyBase64)
    {
        var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
        var privateKey = new X25519PrivateKeyParameters(privateKeyBytes, 0);
        var publicKey = privateKey.GeneratePublicKey();
        return Convert.ToBase64String(publicKey.GetEncoded());
    }
}