using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class RequestMiddlewareRegistrationValidatorTests
{
    [Fact(DisplayName = "ValidateBehaviorRegistration rejects invalid expected interface type")]
    public void ValidateBehaviorRegistrationShouldRejectInvalidExpectedInterfaceType()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(TestBeforeBehavior<,>),
            typeof(IDisposable),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Expected behavior interface '*' must be an open generic interface.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects abstract middleware")]
    public void ValidateBehaviorRegistrationShouldRejectAbstractMiddleware()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(AbstractBeforeBehavior<,>),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must not be abstract.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects interface middleware")]
    public void ValidateBehaviorRegistrationShouldRejectInterfaceMiddleware()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(IBeforeBehaviorContract<,>),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must be a class, not an interface.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects partially open middleware")]
    public void ValidateBehaviorRegistrationShouldRejectPartiallyOpenMiddleware()
    {
        var middlewareType = typeof(Dictionary<,>).MakeGenericType(
            typeof(string),
            typeof(List<>));

        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            middlewareType,
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must be either a closed type or an open generic type definition.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects open generic middleware with invalid parameter count")]
    public void ValidateBehaviorRegistrationShouldRejectOpenGenericMiddlewareWithInvalidParameterCount()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(ThreeParameterBeforeBehavior<,,>),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must have exactly 2 generic parameters.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects middleware without a single public constructor")]
    public void ValidateBehaviorRegistrationShouldRejectMiddlewareWithoutSinglePublicConstructor()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(BehaviorWithMultiplePublicConstructors),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Middleware '*' must have exactly one public constructor.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects middleware without expected contract")]
    public void ValidateBehaviorRegistrationShouldRejectMiddlewareWithoutExpectedContract()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(PlainMiddleware),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must implement '*IBeforeRequestBehavior*'.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration rejects open generic middleware without expected contract")]
    public void ValidateBehaviorRegistrationShouldRejectOpenGenericMiddlewareWithoutExpectedContract()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(PlainGenericMiddleware<,>),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before middleware '*' must implement '*IBeforeRequestBehavior*'.");
    }

    [Fact(DisplayName = "ValidateBehaviorRegistration accepts a valid open generic middleware")]
    public void ValidateBehaviorRegistrationShouldAcceptValidOpenGenericMiddleware()
    {
        var act = () => RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            typeof(TestBeforeBehavior<,>),
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        act.Should().NotThrow();
    }
}
