using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareFrameTests
{
    [Fact(DisplayName = "Request middleware frame generates an optional next frame")]
    public void RequestMiddlewareFrameShouldGenerateOptionalNextFrame()
    {
        var method = GeneratedMethod.ForNoArg("Handle");
        var frame = new TestRequestMiddlewareFrame();

        using var writerWithoutNext = new SourceWriter();
        frame.GenerateCode(method, writerWithoutNext);

        frame.Next = new CommentFrame("next frame");
        using var writerWithNext = new SourceWriter();
        frame.GenerateCode(method, writerWithNext);

        writerWithoutNext.Code().ShouldBeEmpty();
        writerWithNext.Code().ShouldContain("next frame");
    }

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

    private sealed class TestRequestMiddlewareFrame()
        : RequestMiddlewareFrameBase(typeof(TestCommand), [])
    {
        public override void GenerateCode(
            GeneratedMethod method,
            ISourceWriter writer)
        {
            GenerateNextFrame(method, writer);
        }
    }
}
