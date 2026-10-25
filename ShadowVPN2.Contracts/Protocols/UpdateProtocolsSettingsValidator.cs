using FluentValidation;

namespace ShadowVPN2.Contracts.Protocols;

public sealed class UpdateProtocolsSettingsValidator : AbstractValidator<UpdateProtocolsSettingsRequest> {
    public UpdateProtocolsSettingsValidator() {
        RuleForEach(x => x.Protocols).SetInheritanceValidator(validators =>
            validators.Add<AwgProtocolSettingsDto>(new AwgSettingsValidator()));
    }
}