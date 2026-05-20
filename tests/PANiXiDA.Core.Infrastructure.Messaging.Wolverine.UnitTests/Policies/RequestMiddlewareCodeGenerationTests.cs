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

        resolved.Should().BeTrue();
        closedMiddlewareType.Should().Be<TestBeforeBehavior<TestCommand, Result>>();
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

        resolved.Should().BeTrue();
        closedMiddlewareType.Should().Be<BaseCommandBeforeBehavior>();
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

        resolved.Should().BeFalse();
        closedMiddlewareType.Should().BeNull();
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

        resolved.Should().BeFalse();
        closedMiddlewareType.Should().BeNull();
    }

    [Fact(DisplayName = "TryResolveClosedMiddlewareType throws for open generic middleware with invalid parameter count")]
    public void TryResolveClosedMiddlewareTypeShouldThrowForOpenGenericMiddlewareWithInvalidParameterCount()
    {
        var act = () => RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(ThreeParameterBeforeBehavior<,,>),
            typeof(TestCommand),
            typeof(Result),
            typeof(IBeforeRequestBehavior<,>),
            out _);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Open generic middleware '*' must have exactly 2 generic parameters.");
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

        resolved.Should().BeFalse();
        closedMiddlewareType.Should().BeNull();
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

        resolved.Should().BeFalse();
        closedMiddlewareType.Should().BeNull();
    }

    [Fact(DisplayName = "ResolveConstructor rejects middleware without a single public constructor")]
    public void ResolveConstructorShouldRejectMiddlewareWithoutSinglePublicConstructor()
    {
        var act = () => RequestMiddlewareCodeGeneration.ResolveConstructor(typeof(BehaviorWithoutPublicConstructor));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Type '*BehaviorWithoutPublicConstructor' must have exactly one public constructor.");
    }

    [Fact(DisplayName = "ResolveConstructor returns a single public constructor")]
    public void ResolveConstructorShouldReturnSinglePublicConstructor()
    {
        var constructor = RequestMiddlewareCodeGeneration.ResolveConstructor(typeof(ClosedCommandBeforeBehavior));

        constructor.DeclaringType.Should().Be<ClosedCommandBeforeBehavior>();
        constructor.GetParameters().Should().BeEmpty();
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

        variableName.Should().Be(expected);
    }

    [Fact(DisplayName = "ToCamelCase returns fallback for a blank name")]
    public void ToCamelCaseShouldReturnFallbackForBlankValue()
    {
        var method = typeof(RequestMiddlewareCodeGeneration).GetMethod(
            "ToCamelCase",
            BindingFlags.Static | BindingFlags.NonPublic);

        var variableName = method!.Invoke(null, [" "]);

        variableName.Should().Be("middleware");
    }

    [Fact(DisplayName = "GetCodeTypeName returns generated code name for non-generic type")]
    public void GetCodeTypeNameShouldReturnNonGenericTypeNameForGeneratedCode()
    {
        var typeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(typeof(string));

        typeName.Should().Be("System.String");
    }

    [Fact(DisplayName = "GetCodeTypeName returns generated code name for generic type")]
    public void GetCodeTypeNameShouldReturnGenericTypeNameForGeneratedCode()
    {
        var typeName = RequestMiddlewareCodeGeneration.GetCodeTypeName(typeof(Dictionary<string, List<int>>));

        typeName.Should().Be(
            "System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Int32>>");
    }
}
