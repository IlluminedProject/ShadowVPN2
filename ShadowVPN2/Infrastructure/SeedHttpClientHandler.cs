using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Infrastructure;

internal sealed class SeedHttpClientHandler(X509Certificate2 pin) : HttpClientHandler {
    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (disposing)
            pin.Dispose();
    }
}