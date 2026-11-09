using Xunit;

namespace ShadowVPN2.Tests;

internal sealed class RavenServerTheoryAttribute : TheoryAttribute {
    public RavenServerTheoryAttribute() {
        Skip = RavenServerFixture.UnavailableReason;
    }
}