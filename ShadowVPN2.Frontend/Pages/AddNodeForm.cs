using System.ComponentModel.DataAnnotations;
using System.Text;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Frontend.Pages;

public sealed class AddNodeForm {
    public string Name { get; set; } = "new-node";
    public HostName? Domain { get; set; }

    [Range(1, 65535, ErrorMessage = "HTTPS port must be between 1 and 65535.")]
    public int WebPort { get; set; }

    public string GenerateEnvironment(string joinToken) {
        return $"""
                WebPort={WebPort}
                JoinToken={joinToken}
                """.ReplaceLineEndings("\n");
    }

    public static async Task<string> ReadComposeAsync() {
        await using var stream = typeof(AddNodeForm).Assembly.GetManifestResourceStream(
            "ShadowVPN2.Frontend.NodeCompose.yaml")!;
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync()).ReplaceLineEndings("\n");
    }

    public static string GenerateStartupCommand(string compose, string environment) {
        var composeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(compose));
        var environmentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(environment));
        return "(umask 077 && " +
               "printf '%s' '" + composeBase64 + "' | base64 -d > compose.yaml && " +
               "printf '%s' '" + environmentBase64 + "' | base64 -d > .env && " +
               "if docker compose version >/dev/null 2>&1; then " +
               "docker compose -f compose.yaml --env-file .env up -d; " +
               "else docker-compose -f compose.yaml --env-file .env up -d; fi)";
    }
}