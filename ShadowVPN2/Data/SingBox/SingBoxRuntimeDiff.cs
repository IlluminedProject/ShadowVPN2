using System.Text.Json;
using System.Text.Json.Nodes;
using ShadowVPN2.Data.SingBox.Models;

namespace ShadowVPN2.Data.SingBox;

public static class SingBoxRuntimeDiff {
    public static bool IsRuntimeOnlyChange(SingBoxConfig applied, SingBoxConfig candidate) {
        var left = JsonSerializer.SerializeToNode(applied, SingBoxService.SerializerOptions)!;
        var right = JsonSerializer.SerializeToNode(candidate, SingBoxService.SerializerOptions)!;
        if (JsonNode.DeepEquals(left, right)) return false;
        Normalize(left, applied);
        Normalize(right, candidate);
        return JsonNode.DeepEquals(left, right);
    }

    private static void Normalize(JsonNode json, SingBoxConfig config) {
        NormalizeSections(json["inbounds"]!.AsArray(), config.Inbounds);
        NormalizeSections(json["endpoints"]!.AsArray(), config.Endpoints);
    }

    private static void NormalizeSections<T>(JsonArray nodes, IReadOnlyList<T> sections) {
        for (var index = 0; index < sections.Count; index++) {
            if (sections[index] is not IRuntimeManagedConfig runtime) continue;
            foreach (var property in runtime.RuntimeManagedProperties)
                nodes[index]!.AsObject().Remove(property);
        }
    }
}