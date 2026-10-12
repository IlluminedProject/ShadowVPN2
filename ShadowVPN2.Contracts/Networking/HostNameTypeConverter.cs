using System.ComponentModel;
using System.Globalization;

namespace ShadowVPN2.Contracts.Networking;

public sealed class HostNameTypeConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        return value is string text ? HostName.Parse(text) : base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value,
        Type destinationType) {
        return destinationType == typeof(string) && value is HostName host
            ? host.Value
            : base.ConvertTo(context, culture, value, destinationType);
    }
}