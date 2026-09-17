using System.Reflection;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public static class ProtocolDefinitionMetadata {
    public static ProtocolSocketKind GetSocketKind(ProtocolGlobalSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);

        var type = settings.GetType();
        if (!typeof(IProtocolDefinition).IsAssignableFrom(type)) {
            throw new InvalidOperationException(
                $"Protocol {type.Name} does not implement {nameof(IProtocolDefinition)}");
        }

        var property = type.GetProperty(nameof(IProtocolDefinition.SocketKind),
            BindingFlags.Public | BindingFlags.Static);
        if (property?.GetValue(null) is ProtocolSocketKind socketKind)
            return socketKind;

        throw new InvalidOperationException(
            $"Protocol {type.Name} does not define static {nameof(IProtocolDefinition.SocketKind)}");
    }
}