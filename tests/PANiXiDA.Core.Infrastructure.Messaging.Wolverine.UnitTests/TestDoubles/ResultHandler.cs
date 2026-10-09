namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class ResultHandler : PANiXiDA.Core.Application.Messaging.Mediator.Handlers.IRequestHandler<TestCommand, Result>
{
    public Task<Result> HandleAsync(TestCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Handle(request));
    }

    public static Result Handle(TestCommand command)
    {
        _ = command;

        return Result.Success();
    }

    public static Result HandleAgain(TestCommand command)
    {
        return Handle(command);
    }
}
