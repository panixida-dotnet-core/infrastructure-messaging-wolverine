using FluentValidation;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Validators;

public sealed class CreateIntegrationRecordCommandValidator : AbstractValidator<CreateIntegrationRecordCommand>
{
    public CreateIntegrationRecordCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty();
    }
}
