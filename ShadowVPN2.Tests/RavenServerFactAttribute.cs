using Xunit;

namespace ShadowVPN2.Tests;

internal sealed class RavenServerFactAttribute : FactAttribute {
    public RavenServerFactAttribute() {
        Skip = RavenServerFixture.UnavailableReason;
    }
}