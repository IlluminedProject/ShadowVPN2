using System.Globalization;
using FluentValidation;

namespace ShadowVPN2.Contracts.Protocols;

public sealed class AwgSettingsValidator : AbstractValidator<IAwgSettings> {
    private const string HeaderMessage =
        "{PropertyName} must be an integer between 0 and 4294967295 or an ascending min-max range within these bounds";

    private const string OverlapMessage = "{PropertyName} must not overlap another header range";

    public AwgSettingsValidator() {
        RuleFor(x => x.S1).InclusiveBetween(0, ushort.MaxValue);
        RuleFor(x => x.S2).InclusiveBetween(0, ushort.MaxValue);
        RuleFor(x => x.S3).InclusiveBetween(0, ushort.MaxValue);
        RuleFor(x => x.S4).InclusiveBetween(0, ushort.MaxValue);
        RuleFor(x => x.H1).Must(IsHeader).WithMessage(HeaderMessage)
            .Must((model, value) => DoesNotOverlap(value, model.H2, model.H3, model.H4)).WithMessage(OverlapMessage);
        RuleFor(x => x.H2).Must(IsHeader).WithMessage(HeaderMessage)
            .Must((model, value) => DoesNotOverlap(value, model.H1, model.H3, model.H4)).WithMessage(OverlapMessage);
        RuleFor(x => x.H3).Must(IsHeader).WithMessage(HeaderMessage)
            .Must((model, value) => DoesNotOverlap(value, model.H1, model.H2, model.H4)).WithMessage(OverlapMessage);
        RuleFor(x => x.H4).Must(IsHeader).WithMessage(HeaderMessage)
            .Must((model, value) => DoesNotOverlap(value, model.H1, model.H2, model.H3)).WithMessage(OverlapMessage);
    }

    private static bool IsHeader(string? value) {
        return TryParseHeader(value, out _);
    }

    private static bool DoesNotOverlap(string? value, string? first, string? second, string? third) {
        if (!TryParseHeader(value, out var range))
            return true;
        return !Overlaps(range, first) && !Overlaps(range, second) && !Overlaps(range, third);
    }

    private static bool Overlaps((uint Min, uint Max) range, string? other) {
        return TryParseHeader(other, out var candidate) && range.Min <= candidate.Max && candidate.Min <= range.Max;
    }

    private static bool TryParseHeader(string? value, out (uint Min, uint Max) range) {
        range = default;
        if (value is null)
            return false;
        var text = value.AsSpan();
        var separator = text.IndexOf('-');
        if (separator < 0) {
            if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return false;
            range = (number, number);
            return true;
        }

        if (!uint.TryParse(text[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var min) ||
            !uint.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var max) ||
            max < min)
            return false;
        range = (min, max);
        return true;
    }
}