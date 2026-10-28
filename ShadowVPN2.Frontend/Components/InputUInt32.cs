using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace ShadowVPN2.Frontend.Components;

public sealed class InputUInt32 : InputBase<uint> {
    protected override void BuildRenderTree(RenderTreeBuilder builder) {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "number");
        builder.AddAttribute(3, "step", "1");
        builder.AddAttribute(4, "name", NameAttributeValue);
        builder.AddAttribute(5, "class", CssClass);
        builder.AddAttribute(6, "value", CurrentValueAsString);
        builder.AddAttribute(7, "onchange", EventCallback.Factory.CreateBinder<string?>(
            this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.CloseElement();
    }

    protected override string FormatValueAsString(uint value) {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    protected override bool TryParseValueFromString(string? value, out uint result,
        [NotNullWhen(false)] out string? validationErrorMessage) {
        if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) {
            validationErrorMessage = null;
            return true;
        }

        validationErrorMessage =
            $"{DisplayName ?? FieldIdentifier.FieldName} must be an integer between 0 and 4294967295";
        return false;
    }
}