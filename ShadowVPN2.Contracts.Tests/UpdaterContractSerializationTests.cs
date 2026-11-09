using System.Text.Json;
using ShadowVPN2.Contracts.Updater;
using Xunit;

namespace ShadowVPN2.Contracts.Tests;

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
}