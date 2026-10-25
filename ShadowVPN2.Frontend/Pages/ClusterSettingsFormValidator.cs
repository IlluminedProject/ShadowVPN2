using FluentValidation;
using ShadowVPN2.Contracts.Protocols;

namespace ShadowVPN2.Frontend.Pages;

public sealed class ClusterSettingsFormValidator : AbstractValidator<ClusterSettingsForm> {
    public ClusterSettingsFormValidator() {
        RuleFor(x => x.Awg).SetValidator(new AwgSettingsValidator());
    }
}