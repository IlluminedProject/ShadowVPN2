using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Updater;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UpdaterRequest))]
[JsonSerializable(typeof(UpdaterResponse))]
public partial class UpdaterJsonContext : JsonSerializerContext;