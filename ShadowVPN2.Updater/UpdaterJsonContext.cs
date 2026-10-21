using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Updater;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UpdaterSocketRequest))]
[JsonSerializable(typeof(UpdaterSocketResponse))]
internal partial class UpdaterJsonContext : JsonSerializerContext;