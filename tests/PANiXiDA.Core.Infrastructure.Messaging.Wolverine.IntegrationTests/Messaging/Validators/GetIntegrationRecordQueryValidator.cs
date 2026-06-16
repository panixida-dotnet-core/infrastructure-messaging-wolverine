using FluentValidation;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Validators;

public sealed class GetIntegrationRecordQueryValidator : AbstractValidator<GetIntegrationRecordQuery>
{
    public GetIntegrationRecordQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
    }
}
