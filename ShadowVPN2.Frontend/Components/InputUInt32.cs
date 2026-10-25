using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components.Forms;

namespace ShadowVPN2.Frontend.Components;

public sealed class InputUInt32 : InputNumber<uint> {
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