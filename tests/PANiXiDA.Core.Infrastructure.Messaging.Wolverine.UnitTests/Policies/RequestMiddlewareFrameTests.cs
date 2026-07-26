using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareFrameTests
{
    [Fact(DisplayName = "Before frame skips middleware for another request type")]
    public void BeforeFrameShouldSkipMiddlewareForAnotherRequestType()
    {
        var frame = BeforeRequestMiddlewareFrame.TryCreate(
            typeof(OtherCommand),
            typeof(Result),
            [typeof(ClosedCommandBeforeBehavior)]);

        frame.ShouldBeNull();
    }

    [Fact(DisplayName = "After frame skips middleware for another request type")]
    public void AfterFrameShouldSkipMiddlewareForAnotherRequestType()
    {
        var resultVariable = new Variable(typeof(Result), "result");

        var frame = AfterRequestMiddlewareFrame.TryCreate(
            typeof(OtherCommand),
            resultVariable,
            [typeof(ClosedCommandAfterBehavior)]);

        frame.ShouldBeNull();
    }
}
