using System.Text.Json.Serialization;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;

namespace ShadowVPN2.Infrastructure.Containers.Registry;

[JsonSerializable(typeof(RegistryManifest))]
[JsonSerializable(typeof(RegistryImageConfiguration))]
[JsonSerializable(typeof(RegistryTokenResponse))]
internal partial class RegistryJsonContext : JsonSerializerContext;