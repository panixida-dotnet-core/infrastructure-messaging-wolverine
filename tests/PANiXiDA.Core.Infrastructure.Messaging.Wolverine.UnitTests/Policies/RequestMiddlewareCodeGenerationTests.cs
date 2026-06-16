using System.Reflection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareCodeGenerationTests
{
    [Fact(DisplayName = "TryResolveClosedMiddlewareType closes generic middleware for request and result")]
    public void TryResolveClosedMiddlewareTypeShouldCloseGenericMiddlewareForRequestAndResult()
    {
        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(TestBeforeBehavior<,>),
            typeof(TestCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeTrue();
        closedMiddlewareType.ShouldBe(typeof(TestBeforeBehavior<TestCommand, Result>));
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType returns compatible closed middleware")]
    public void TryResolveClosedMiddlewareTypeShouldUseCompatibleClosedMiddleware()
    {
        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(BaseCommandBeforeBehavior),
            typeof(DerivedCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeTrue();
        closedMiddlewareType.ShouldBe(typeof(BaseCommandBeforeBehavior));
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType returns false for incompatible closed middleware")]
    public void TryResolveClosedMiddlewareTypeShouldReturnFalseForIncompatibleClosedMiddleware()
    {
        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(ClosedCommandBeforeBehavior),
            typeof(OtherCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeFalse();
        closedMiddlewareType.ShouldBeNull();
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType returns false for partially open middleware")]
    public void TryResolveClosedMiddlewareTypeShouldReturnFalseForPartiallyOpenMiddleware()
    {
        var middlewareType = typeof(Dictionary<,>).MakeGenericType(
            typeof(string),
            typeof(List<>));

        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            middlewareType,
            typeof(TestCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeFalse();
        closedMiddlewareType.ShouldBeNull();
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType throws for open generic middleware with invalid parameter count")]
    public void TryResolveClosedMiddlewareTypeShouldThrowForOpenGenericMiddlewareWithInvalidParameterCount()
    {
        var act = () =>
        {
            RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
                typeof(ThreeParameterBeforeBehavior<,,>),
                typeof(TestCommand),
                typeof(Result),
                typeof(IBeforeRequestBehavior<,>),
                out _);
        };

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldStartWith("Open generic middleware '");
        exception.Message.ShouldEndWith("' must have exactly 2 generic parameters.");
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType returns false when generic constraints do not match")]
    public void TryResolveClosedMiddlewareTypeShouldReturnFalseWhenGenericConstraintsDoNotMatch()
    {
        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(ConstrainedBeforeBehavior<,>),
            typeof(int),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeFalse();
        closedMiddlewareType.ShouldBeNull();
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType returns false when open generic middleware does not support contract")]
    public void TryResolveClosedMiddlewareTypeShouldReturnFalseWhenOpenGenericMiddlewareDoesNotSupportContract()
    {
        var resolved = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(TestAfterBehavior<,>),
            typeof(TestCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out var closedMiddlewareType);

        resolved.ShouldBeFalse();
        closedMiddlewareType.ShouldBeNull();
    }

    [Fact(DisplayName = "ResolveConstructor rejects middleware without a single public constructor")]
    public void ResolveConstructorShouldRejectMiddlewareWithoutSinglePublicConstructor()
    {
        var act = () => RequestMiddlewareCodeGeneration.ResolveConstructor(typeof(BehaviorWithoutPublicConstructor));

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldContain("BehaviorWithoutPublicConstructor");
        exception.Message.ShouldEndWith("' must have exactly one public constructor.");
    }

    [Fact(DisplayName = "ResolveConstructor returns a single public constructor")]
    public void ResolveConstructorShouldReturnSinglePublicConstructor()
    {
        var constructor = RequestMiddlewareCodeGeneration.ResolveConstructor(typeof(ClosedCommandBeforeBehavior));

        constructor.DeclaringType.ShouldBe(typeof(ClosedCommandBeforeBehavior));
        constructor.GetParameters().ShouldBeEmpty();
    }

    [Theory(DisplayName = "BuildVariableName builds stable variable names")]
    [InlineData(typeof(TestBeforeBehavior<,>), "abc123", "testBeforeBehavior_abc123")]
    [InlineData(typeof(X), "1", "x_1")]
    public void BuildVariableNameShouldUseFriendlyTypeNameAndSuffix(
        Type type,
        string suffix,
        string expected)
    {
        var variableName = RequestMiddlewareCodeGeneration.BuildVariableName(type, suffix);

        variableName.ShouldBe(expected);
    }

    [Fact(DisplayName = "ToCamelCase returns fallback for a blank name")]
    public void ToCamelCaseShouldReturnFallbackForBlankValue()
    {
        var method = typeof(RequestMiddlewareCodeGeneration).GetMethod(
            "ToCamelCase",
            BindingFlags.Static | BindingFlags.NonPublic);

        var variableName = method!.Invoke(null, [" "]);

        variableName.ShouldBe("middleware");
    }

    [Fact(DisplayName = "GetCodeTypeName returns generated code name for non-generic type")]
    public void GetCodeTypeNameShouldReturnNonGenericTypeNameForGeneratedCode()
    {
        var typeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(typeof(string));

        typeName.ShouldBe("System.String");
    }

    [Fact(DisplayName = "GetCodeTypeName returns generated code name for generic type")]
    public void GetCodeTypeNameShouldReturnGenericTypeNameForGeneratedCode()
    {
        var typeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(typeof(Dictionary<string, List<int>>));

        typeName.ShouldBe(
            "System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Int32>>");
    }

    [Fact(DisplayName = "BuildFailureResultCode returns failed result expression")]
    public void BuildFailureResultCodeShouldReturnFailedResultExpression()
    {
        var code = RequestMiddlewareCodeGeneration.BuildFailureResultCode(
            typeof(Result),
            "beforeResult");

        code.ShouldBe("beforeResult");
    }

    [Fact(DisplayName = "BuildFailureResultCode converts failed result to generic result")]
    public void BuildFailureResultCodeShouldConvertFailedResultToGenericResult()
    {
        var code = RequestMiddlewareCodeGeneration.BuildFailureResultCode(
            typeof(Result<TestQueryView>),
            "beforeResult");

        code.ShouldBe(
            "global::PANiXiDA.Core.ResultPattern.Result.Failure<PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages.TestQueryView>(beforeResult.Errors)");
    }
}
