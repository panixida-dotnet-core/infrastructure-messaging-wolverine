using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareFrameTests
{
    [Fact(DisplayName = "Handler call produces a shared result and continues once without middleware")]
    public void HandlerCallShouldContinueWithoutMiddleware()
    {
        var frame = new RequestMiddlewareHandlerCall(
            typeof(TestCommand),
            new MethodCall(typeof(ResultHandler), nameof(ResultHandler.Handle)),
            RequestMiddlewareRegistry.Empty);
        var method = GeneratedMethod.ForNoArg("Handle");
        _ = frame.FindVariables(new TestMethodVariables()).ToArray();
        using var writer = new SourceWriter();

        frame.GenerateCode(method, writer);

        var code = writer.Code();
        code.ShouldContain($"{frame.ReturnVariable!.Usage} = default!;");
        code.ShouldContain($"if ({frame.ReturnVariable.Usage} is null)");
        code.ShouldContain($"{frame.ReturnVariable.Usage} =");
        code.ShouldNotContain("finally");
        VerifyOptionalNextFrame(frame);
    }

    [Fact(DisplayName = "Handler call retains aliases and emits all request middleware around the handler")]
    public void HandlerCallShouldRetainAliasesAndGenerateMiddleware()
    {
        var handlerType = typeof(TestRequestHandler<TestCommand, Result>);
        var handler = new MethodCall(handlerType, "HandleAsync");
        var handlerContract = typeof(PANiXiDA.Core.Application.Messaging.Mediator.Handlers.IRequestHandler<TestCommand, Result>);
        handler.Aliases.Add(handlerContract, handlerType);
        var registry = RequestMiddlewareRegistry.Create(builder => builder
            .AddBefore<ClosedCommandBeforeBehavior>()
            .AddAfter<ClosedCommandAfterBehavior>()
            .AddFinally(typeof(TestFinallyBehavior<,>)));
        var frame = new RequestMiddlewareHandlerCall(typeof(TestCommand), handler, registry);
        _ = frame.FindVariables(new TestMethodVariables()).ToArray();
        using var writer = new SourceWriter();

        frame.GenerateCode(GeneratedMethod.ForNoArg("Handle"), writer);

        frame.Aliases[handlerContract].ShouldBe(handlerType);
        var code = writer.Code();
        code.ShouldContain("BeforeAsync");
        code.ShouldContain("AfterAsync");
        code.ShouldContain("FinallyAsync");
        code.IndexOf("BeforeAsync", StringComparison.Ordinal).ShouldBeLessThan(code.IndexOf("HandleAsync", StringComparison.Ordinal));
        code.IndexOf("HandleAsync", StringComparison.Ordinal).ShouldBeLessThan(code.IndexOf("AfterAsync", StringComparison.Ordinal));
        code.IndexOf("AfterAsync", StringComparison.Ordinal).ShouldBeLessThan(code.IndexOf("FinallyAsync", StringComparison.Ordinal));
        code.ShouldNotContain("EnqueueCascadingAsync");
    }

    [Fact(DisplayName = "Before frame generates its optional next frame")]
    public void BeforeFrameShouldGenerateOptionalNextFrame()
    {
        var frame = BeforeRequestMiddlewareFrame.TryCreate(
            typeof(TestCommand),
            new Variable(typeof(Result), "result"),
            typeof(TestRequestHandler<TestCommand, Result>),
            [typeof(ClosedCommandBeforeBehavior)])
            ?? throw new InvalidOperationException("Before frame was not created.");

        VerifyOptionalNextFrame(frame);
    }

    [Fact(DisplayName = "After frame generates its optional next frame")]
    public void AfterFrameShouldGenerateOptionalNextFrame()
    {
        var resultVariable = new Variable(typeof(Result), "result");
        var frame = AfterRequestMiddlewareFrame.TryCreate(
            typeof(TestCommand),
            resultVariable,
            [typeof(ClosedCommandAfterBehavior)])
            ?? throw new InvalidOperationException("After frame was not created.");

        VerifyOptionalNextFrame(frame);
    }

    [Fact(DisplayName = "Finally frame generates its optional next frame")]
    public void FinallyFrameShouldGenerateOptionalNextFrame()
    {
        var resultVariable = new Variable(typeof(Result), "result");
        var frame = FinallyRequestMiddlewareFrame.TryCreate(
            typeof(TestCommand),
            resultVariable,
            [typeof(TestFinallyBehavior<,>)])
            ?? throw new InvalidOperationException("Finally frame was not created.");

        VerifyOptionalNextFrame(frame);
    }

    [Fact(DisplayName = "Before frame skips middleware for another request type")]
    public void BeforeFrameShouldSkipMiddlewareForAnotherRequestType()
    {
        var frame = BeforeRequestMiddlewareFrame.TryCreate(
            typeof(OtherCommand),
            new Variable(typeof(Result), "result"),
            typeof(TestRequestHandler<OtherCommand, Result>),
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

    private static void VerifyOptionalNextFrame(Frame frame)
    {
        var method = GeneratedMethod.ForNoArg("Handle");
        _ = frame.FindVariables(new TestMethodVariables()).ToArray();

        using var writerWithoutNext = new SourceWriter();
        frame.GenerateCode(method, writerWithoutNext);

        frame.Next = new CommentFrame("next frame");
        using var writerWithNext = new SourceWriter();
        frame.GenerateCode(method, writerWithNext);

        writerWithoutNext.Code().ShouldNotContain("next frame");
        writerWithNext.Code().ShouldContain("next frame");
    }

    private sealed class TestMethodVariables : IMethodVariables
    {
        public Variable FindVariable(Type type)
        {
            return new Variable(type, $"{type.Name}Variable");
        }

        public Variable FindVariable(System.Reflection.ParameterInfo parameter)
        {
            return FindVariable(parameter.ParameterType);
        }

        public Variable FindVariableByName(Type dependency, string name)
        {
            return new Variable(dependency, name);
        }

        public bool TryFindVariableByName(
            Type dependency,
            string name,
            out Variable variable)
        {
            variable = FindVariableByName(dependency, name);
            return true;
        }

        public Variable TryFindVariable(Type type, VariableSource source)
        {
            return FindVariable(type);
        }
    }
}
