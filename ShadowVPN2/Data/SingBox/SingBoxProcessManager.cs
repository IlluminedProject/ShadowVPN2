using Microsoft.Extensions.Options;
using ShadowVPN2.Infrastructure;

namespace ShadowVPN2.Data.SingBox;

public sealed class SingBoxProcessManager(ILogger<SingBoxProcessManager> logger, IOptions<SingBoxOptions> options) {
    private readonly string _binaryPath = options.Value.BinaryPath ?? "sing-box";

    public SingBoxProcessHandle Create(string identifier) {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) {
            throw new ArgumentException(
                "Sing-box identifier must be a single path segment containing letters, digits, '-' or '_'",
                nameof(identifier));
        }

        return new SingBoxProcessHandle(logger, _binaryPath, DataUtils.DataFolder / "sing-box" / identifier);
    }
}