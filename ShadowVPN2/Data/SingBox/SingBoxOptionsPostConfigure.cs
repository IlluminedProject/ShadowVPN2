using Microsoft.Extensions.Options;

namespace ShadowVPN2.Data.SingBox;

public sealed class SingBoxOptionsPostConfigure(
    AwgTunCapabilityProbe tunCapabilityProbe,
    IConfiguration configuration,
    ILogger<SingBoxOptionsPostConfigure>? logger) : IPostConfigureOptions<SingBoxOptions> {
    private int _logged;

    public void PostConfigure(string? name, SingBoxOptions options) {
        var configuredValue = configuration["SingBox:Awg:UseIntegratedTun"];
        if (bool.TryParse(configuredValue, out var useIntegratedTun)) {
            options.Awg.UseIntegratedTun = useIntegratedTun;
            LogOnce("configured", useIntegratedTun);
            return;
        }

        options.Awg.UseIntegratedTun = tunCapabilityProbe.IsSupported();
        LogOnce("probe", options.Awg.UseIntegratedTun);
    }

    private void LogOnce(string source, bool useIntegratedTun) {
        if (Interlocked.Exchange(ref _logged, 1) != 0)
            return;

        logger?.LogInformation("AWG integrated TUN selection: {Source}, UseIntegratedTun={UseIntegratedTun}",
            source, useIntegratedTun);
    }
}