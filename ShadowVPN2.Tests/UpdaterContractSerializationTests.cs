using System.Text.Json;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Updater.Docker;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class UpdaterContractSerializationTests {
    [Fact]
    public void Updater_request_and_response_use_string_enum_wire_values() {
        var request = new UpdaterRequest {
            Operation = UpdaterOperation.Prepare,
            UpdateId = Guid.NewGuid(),
            ImageDigest = "sha256:" + new string('a', 64)
        };
        var requestJson = JsonSerializer.Serialize(request, UpdaterJsonContext.Default.UpdaterRequest);
        Assert.Contains("\"operation\":\"Prepare\"", requestJson);

        var response = new UpdaterResponse
            { UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Prepared };
        var responseJson = JsonSerializer.Serialize(response, UpdaterJsonContext.Default.UpdaterResponse);
        var restored = JsonSerializer.Deserialize(responseJson, UpdaterJsonContext.Default.UpdaterResponse);

        Assert.NotNull(restored);
        Assert.Equal(UpdaterOperation.Prepare,
            JsonSerializer.Deserialize(requestJson, UpdaterJsonContext.Default.UpdaterRequest)!.Operation);
        Assert.Equal(UpdaterStatus.Prepared, restored!.Status);
    }

    [Fact]
    public void Docker_api_models_use_source_generated_json_metadata() {
        const string inspectJson =
            "{\"Id\":\"container-id\",\"Image\":\"image-id\",\"State\":{\"Running\":true,\"Health\":{\"Status\":\"healthy\"}},\"Config\":{\"Image\":\"ghcr.io/illuminedproject/shadowvpn2:latest\",\"Labels\":{\"com.docker.compose.service\":\"shadowvpn2\"}},\"HostConfig\":{\"RestartPolicy\":{\"Name\":\"unless-stopped\",\"MaximumRetryCount\":0}}}";
        var container = JsonSerializer.Deserialize(inspectJson, DockerJsonContext.Default.DockerContainer);

        Assert.NotNull(container);
        Assert.True(container!.State.Running);
        Assert.Equal("healthy", container.State.Health!.Status);

        var request = new DockerCreateRequest {
            Image = "ghcr.io/illuminedproject/shadowvpn2@sha256:" + new string('a', 64),
            HostConfig = new DockerCreateHostConfig {
                Name = "unless-stopped"
            }
        };
        var requestJson = JsonSerializer.Serialize(request, DockerJsonContext.Default.DockerCreateRequest);

        Assert.Contains("\"Image\":\"ghcr.io/illuminedproject/shadowvpn2@sha256:" + new string('a', 64) + "\"",
            requestJson);
        Assert.Contains("\"HostConfig\":{\"Name\":\"unless-stopped\",\"MaximumRetryCount\":0}", requestJson);
    }
}