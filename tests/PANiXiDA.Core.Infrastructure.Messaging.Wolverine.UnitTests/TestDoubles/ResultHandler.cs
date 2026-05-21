namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public static class ResultHandler
{
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
