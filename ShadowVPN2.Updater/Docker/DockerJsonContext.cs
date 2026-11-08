using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Updater.Docker;

[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(DockerContainer))]
[JsonSerializable(typeof(DockerImage))]
[JsonSerializable(typeof(DockerCreateRequest))]
[JsonSerializable(typeof(DockerCreateHostConfig))]
[JsonSerializable(typeof(DockerCreateResponse))]
[JsonSerializable(typeof(DockerRestartPolicy))]
public partial class DockerJsonContext : JsonSerializerContext;