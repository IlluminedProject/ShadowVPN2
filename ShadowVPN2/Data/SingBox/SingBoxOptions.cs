namespace ShadowVPN2.Data.SingBox;

public sealed class SingBoxOptions {
    public string? BinaryPath { get; set; }

    public AwgOptions Awg { get; set; } = new();
}

public sealed class AwgOptions {
    public bool UseIntegratedTun { get; set; }
}