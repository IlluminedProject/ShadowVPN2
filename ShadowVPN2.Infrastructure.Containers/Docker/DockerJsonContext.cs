using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowVPN2.Infrastructure.Containers.Docker.Models;

namespace ShadowVPN2.Infrastructure.Containers.Docker;

[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(DockerContainer))]
[JsonSerializable(typeof(DockerImage))]
[JsonSerializable(typeof(DockerCreateRequest))]
[JsonSerializable(typeof(DockerCreateHostConfig))]
[JsonSerializable(typeof(DockerCreateResponse))]
[JsonSerializable(typeof(DockerRestartPolicy))]
internal partial class DockerJsonContext : JsonSerializerContext;