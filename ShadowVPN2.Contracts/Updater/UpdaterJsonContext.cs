using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Updater;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UpdaterRequest))]
[JsonSerializable(typeof(UpdaterResponse))]
[JsonSerializable(typeof(ContainerImageInfo))]
public partial class UpdaterJsonContext : JsonSerializerContext;
