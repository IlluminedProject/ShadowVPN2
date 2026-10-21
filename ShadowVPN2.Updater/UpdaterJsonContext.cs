using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Updater;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UpdaterSocketRequest))]
[JsonSerializable(typeof(UpdaterSocketResponse))]
[JsonSerializable(typeof(ContainerImageInfo))]
internal partial class UpdaterJsonContext : JsonSerializerContext;